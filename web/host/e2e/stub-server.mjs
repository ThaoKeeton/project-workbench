import { createHash } from "node:crypto";
import { createServer } from "node:http";

const token = "preview-session";
const port = 4317;
const sampleId = "com.projectworkbench.test-cases.v2";
const magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
const sockets = new Set();

const defaultCases = () => ({
  schema: "workbench.test-cases.v2.preview",
  schemaVersion: 2,
  suiteName: "原生工作台演示",
  rows: [
    { id: "EX-001", action: "启用本地插件", expected: "页面出现在主窗口" },
    { id: "EX-002", action: "编辑摘要示例", expected: "草稿保存到本地数据目录" },
    { id: "EX-003", action: "停用示例插件", expected: "页面与命令入口撤销" },
  ],
});

let state = fresh();

function fresh() {
  return {
    generation: 1,
    revision: "rev-1",
    activeProjectId: "default",
    projects: [
      { id: "default", name: "默认示例", isArchived: false },
      { id: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", name: "示例项目", isArchived: false },
    ],
    cases: {},
    themeSetting: "system",
  };
}

function readToken(request) {
  const header = request.headers.authorization ?? "";
  if (header.startsWith("Bearer ")) return header.slice("Bearer ".length).trim();
  const cookie = request.headers.cookie ?? "";
  const pair = cookie.split(";").map((part) => part.trim()).find((part) => part.startsWith("wb_session="));
  return pair ? decodeURIComponent(pair.slice("wb_session=".length)) : "";
}

function sendJson(response, status, body, extra = {}) {
  const payload = JSON.stringify(body);
  response.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": Buffer.byteLength(payload),
    "Cache-Control": "no-store",
    ...extra,
  });
  response.end(payload);
}

function projectsBody() {
  return {
    schemaVersion: 1,
    revision: state.revision,
    activeProjectId: state.activeProjectId,
    projects: state.projects,
  };
}

function casesFor(projectId) {
  return state.cases[projectId] ?? defaultCases();
}

function pluginsBody() {
  return {
    plugins: [
      {
        id: "com.projectworkbench.summary",
        name: "工作摘要 · 示例",
        version: "0.1.0",
        contract: "v1",
        state: "Disabled",
        trusted: false,
        loaded: false,
        diagnostics: [],
        permissions: [],
        uiPath: null,
      },
      {
        id: sampleId,
        name: "测试用例 · 示例",
        version: "0.1.0",
        contract: "v2-preview",
        state: "Ready",
        trusted: true,
        loaded: true,
        diagnostics: [],
        permissions: [
          { id: "project.read", sensitive: false },
          { id: "plugin.data.read", sensitive: false },
          { id: "plugin.data.write", sensitive: false },
          { id: "local.file.export", sensitive: false },
        ],
        uiPath: "/plugins/test-cases",
      },
    ],
  };
}

function diagnosticsBody() {
  return {
    schema: "workbench.diagnostics.v2.preview",
    service: { bind: "loopback", lanEnabled: false, autoSend: false },
    plugins: { discovered: 2, ready: 1, invalid: 0, loaded: 1 },
    projects: { count: state.projects.length, activePresent: true },
    issues: [],
    nativeWindow: { minimumWidth: 1100 },
  };
}

function infoBody() {
  return {
    product: "ProjectWorkbench",
    host: "local-service",
    apiVersion: "2.0.0-preview.1",
    preview: true,
    device: { id: "local", label: "本机", kind: "loopback" },
    lan: { enabled: false, availableInPreview: false },
    autoSend: false,
    catalogSchemaVersion: 1,
    serviceVersion: "0.5.0-preview.1",
  };
}

function csvFor(document) {
  const lines = ["suite,id,action,expected", ...document.rows.map((row) => [document.suiteName, row.id, row.action, row.expected].join(","))];
  return `\uFEFF${lines.join("\r\n")}\r\n`;
}

function broadcast(message) {
  const frame = textFrame(JSON.stringify(message));
  for (const socket of sockets) socket.write(frame);
}

function textFrame(text) {
  const payload = Buffer.from(text);
  const header = Buffer.alloc(payload.length < 126 ? 2 : 4);
  header[0] = 0x81;
  if (payload.length < 126) header[1] = payload.length;
  else {
    header[1] = 126;
    header.writeUInt16BE(payload.length, 2);
  }
  return Buffer.concat([header, payload]);
}

function readBody(request) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    request.on("data", (chunk) => chunks.push(chunk));
    request.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    request.on("error", reject);
  });
}

function validCases(body) {
  if (!body || body.schema !== "workbench.test-cases.v2.preview" || body.schemaVersion !== 2) return null;
  if (typeof body.suiteName !== "string" || body.suiteName.length === 0 || body.suiteName.length > 80) return null;
  if (!Array.isArray(body.rows) || body.rows.length !== 3) return null;
  const ids = ["EX-001", "EX-002", "EX-003"];
  for (let index = 0; index < ids.length; index += 1) {
    const row = body.rows[index];
    if (!row || row.id !== ids[index] || typeof row.action !== "string" || typeof row.expected !== "string") return null;
    if (row.action.length === 0 || row.action.length > 500 || row.expected.length === 0 || row.expected.length > 500) return null;
  }
  return { schema: body.schema, schemaVersion: 2, suiteName: body.suiteName, rows: body.rows.map((row) => ({ id: row.id, action: row.action, expected: row.expected })) };
}

const server = createServer(async (request, response) => {
  const url = new URL(request.url ?? "/", "http://127.0.0.1");
  if (request.method === "GET" && url.pathname === "/ready") {
    response.writeHead(200, { "Content-Type": "text/plain" });
    response.end("ok");
    return;
  }
  if (request.method === "POST" && url.pathname === "/api/v2/__reset") {
    state = fresh();
    sendJson(response, 204, {});
    return;
  }
  if (readToken(request) !== token) {
    sendJson(response, 401, { error: "Unauthorized" });
    return;
  }
  if (request.method === "GET" && url.pathname === "/api/v2/health") return sendJson(response, 200, { status: "ok", apiVersion: "2.0.0-preview.1", bind: "loopback" });
  if (request.method === "GET" && url.pathname === "/api/v2/info") return sendJson(response, 200, infoBody());
  if (request.method === "GET" && url.pathname === "/api/v2/projects") return sendJson(response, 200, projectsBody());
  if (request.method === "GET" && url.pathname === "/api/v2/plugins") return sendJson(response, 200, pluginsBody());
  if (request.method === "GET" && url.pathname === "/api/v2/diagnostics") return sendJson(response, 200, diagnosticsBody());
  if (request.method === "GET" && url.pathname === "/api/v2/ui/theme") {
    return sendJson(response, 200, { schema: "workbench.ui-theme.v2.preview", schemaVersion: 2, setting: state.themeSetting });
  }
  if (request.method === "PUT" && url.pathname === "/api/v2/ui/theme") {
    const body = JSON.parse(await readBody(request));
    if (body.setting !== "system" && body.setting !== "light" && body.setting !== "dark") return sendJson(response, 400, { error: "InvalidTheme" });
    state.themeSetting = body.setting;
    return sendJson(response, 200, { schema: "workbench.ui-theme.v2.preview", schemaVersion: 2, setting: state.themeSetting });
  }
  if (request.method === "POST" && url.pathname === "/api/v2/session") {
    response.writeHead(204, {
      "Cache-Control": "no-store",
      "Set-Cookie": `wb_session=${token}; HttpOnly; SameSite=Strict; Path=/`,
    });
    response.end();
    return;
  }
  if (request.method === "POST" && url.pathname === "/api/v2/projects/active") {
    const body = JSON.parse(await readBody(request));
    const project = state.projects.find((item) => item.id === body.id);
    if (body.expectedRevision !== state.revision) return sendJson(response, 409, { error: "RevisionConflict" });
    if (!project) return sendJson(response, 404, { error: "ProjectNotFound" });
    state.activeProjectId = project.id;
    state.generation += 1;
    state.revision = `rev-${state.generation}`;
    broadcast({ type: "project-changed", activeProjectId: state.activeProjectId });
    return sendJson(response, 200, projectsBody());
  }
  if (url.pathname === `/api/v2/plugins/${sampleId}/cases` && request.method === "GET") {
    return sendJson(response, 200, casesFor(state.activeProjectId));
  }
  if (url.pathname === `/api/v2/plugins/${sampleId}/cases` && request.method === "PUT") {
    const document = validCases(JSON.parse(await readBody(request)));
    if (!document) return sendJson(response, 400, { error: "InvalidPluginData" });
    state.cases[state.activeProjectId] = document;
    return sendJson(response, 200, document);
  }
  if (request.method === "POST" && url.pathname === `/api/v2/plugins/${sampleId}/export`) {
    const csv = csvFor(casesFor(state.activeProjectId));
    response.writeHead(200, {
      "Content-Type": "text/csv; charset=utf-8",
      "Content-Disposition": 'attachment; filename="sample-test-cases.csv"',
      "Cache-Control": "no-store",
    });
    response.end(csv);
    return;
  }
  sendJson(response, 404, { error: "NotFound" });
});

server.on("upgrade", (request, socket) => {
  if (readToken(request) !== token) {
    socket.write("HTTP/1.1 401 Unauthorized\r\nConnection: close\r\n\r\n");
    socket.destroy();
    return;
  }
  const key = request.headers["sec-websocket-key"];
  if (!key) {
    socket.destroy();
    return;
  }
  const accept = createHash("sha1").update(key + magic).digest("base64");
  socket.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n\r\n`);
  sockets.add(socket);
  socket.on("data", () => {});
  socket.on("close", () => sockets.delete(socket));
  socket.on("error", () => sockets.delete(socket));
  socket.write(textFrame(JSON.stringify({ type: "ready", deviceId: "local" })));
});

server.listen(port, "127.0.0.1");
