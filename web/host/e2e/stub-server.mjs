import { createHash } from "node:crypto";
import { createServer } from "node:http";
import { analyzeChat, dashboardDocument, dingImport, dingSample, emptyDigest, emptyInsights, insightState, sendDisabled, upcamImport, upcamSample } from "./preview-plugins.mjs";

const token = "preview-session";
const port = 4317;
const sampleId = "com.projectworkbench.test-cases.v2";
const upcamId = "com.projectworkbench.upcam-standup.v2";
const dingId = "com.projectworkbench.dingtalk-insights.v2";
const testingId = "com.projectworkbench.testing-design.v2";
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
      { id: "default", name: "星河协作", isArchived: false },
      { id: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", name: "灯塔实验", isArchived: false },
    ],
    cases: {},
    digests: {},
    chats: {},
    insights: {},
    themeSetting: "system",
    desk: { pinned: ["upcam", "ding", "test", "runner"], recent: [] },
    imports: [],
    notes: {
      default: { revision: "note-1", text: "只在星河" },
      aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa: { revision: "note-lamp", text: "只在灯塔" },
    },
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

function sameName(left, right) {
  return left.toLocaleLowerCase("en-US") === right.toLocaleLowerCase("en-US");
}

function rejectName(name, exceptId) {
  if (typeof name !== "string" || /[\u0000-\u001f\u007f-\u009f]/.test(name)) return "InvalidName";
  const trimmed = name.trim();
  if (trimmed.length === 0 || trimmed.length > 80) return "InvalidName";
  const taken = state.projects.find((item) => item.id !== exceptId && sameName(item.name, trimmed));
  return taken ? "DuplicateName" : null;
}

function bumpProject() {
  state.generation += 1;
  state.revision = `rev-${state.generation}`;
}

function noteBody() {
  const saved = state.notes[state.activeProjectId];
  if (!saved) return { schemaVersion: 1, revision: "missing", text: "", missing: true };
  return { schemaVersion: 1, revision: saved.revision, text: saved.text, missing: false };
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
      { id: "com.projectworkbench.upcam-standup.v2", name: "UPCAM 站会日报", version: "0.1.0", contract: "v2-preview", state: "Ready", trusted: true, loaded: true, diagnostics: [], permissions: [], uiPath: "/plugin/upcam" },
      { id: "com.projectworkbench.dingtalk-insights.v2", name: "钉钉 Insights", version: "0.1.0", contract: "v2-preview", state: "Ready", trusted: true, loaded: true, diagnostics: [], permissions: [], uiPath: "/plugin/ding" },
      { id: "com.projectworkbench.testing-design.v2", name: "测试设计", version: "0.1.0", contract: "v2-preview", state: "Ready", trusted: true, loaded: true, diagnostics: [], permissions: [], uiPath: "/plugin/test" },
      { id: "com.projectworkbench.test-executor", name: "自动测试", version: "0.1.0", contract: "v2-preview", state: "Ready", trusted: true, loaded: true, diagnostics: [], permissions: [], uiPath: "/plugin/runner" },
      ...state.imports.map((item) => ({ id: item.id, name: item.name, version: item.version, contract: "v2-preview", state: item.enabled ? "Ready" : "Installed", trusted: false, loaded: Boolean(item.enabled), diagnostics: [], permissions: [], uiPath: item.enabled ? `/plugins/local/${item.id}` : null })),
    ],
  };
}

function inspectImport(body) {
  const fileName = typeof body?.fileName === "string" ? body.fileName : "";
  let manifest;
  try { manifest = JSON.parse(body?.text ?? ""); }
  catch { manifest = null; }
  if (!manifest || manifest.contract !== "workbench.plugin.v2" || manifest.contractVersion !== "2.0.0-preview.1" || typeof manifest.id !== "string" || typeof manifest.name !== "string") {
    return { schema: "workbench.plugin-import.v2.preview", schemaVersion: 2, fileName, importable: false, error: "InvalidManifest", plugin: null, checks: [{ id: "manifest", ok: false, text: "这不是插件清单。请选择 plugin.v2.json。" }] };
  }
  const plugin = { id: manifest.id, name: manifest.name, version: manifest.version };
  if (manifest.autoSend) {
    return { schema: "workbench.plugin-import.v2.preview", schemaVersion: 2, fileName, importable: false, error: "AutoSendRejected", plugin, checks: [{ id: "autosend", ok: false, text: "这份清单要求自动发送，不能导入。" }] };
  }
  const taken = new Set(["com.projectworkbench.test-cases.v2", "com.projectworkbench.upcam-standup.v2", "com.projectworkbench.dingtalk-insights.v2", "com.projectworkbench.testing-design.v2", "com.projectworkbench.test-executor", ...state.imports.map((item) => item.id)]);
  if (taken.has(manifest.id)) {
    return { schema: "workbench.plugin-import.v2.preview", schemaVersion: 2, fileName, importable: false, error: "DuplicatePluginId", plugin, checks: [{ id: "duplicate", ok: false, text: "这台电脑上已经有这个插件。" }] };
  }
  const permission = Array.isArray(manifest.permissions) ? manifest.permissions[0] : null;
  const allowed = ["project.read", "plugin.data.read", "plugin.data.write", "local.file.export"];
  if (!permission || permission.sensitive || !allowed.includes(permission.id)) {
    return { schema: "workbench.plugin-import.v2.preview", schemaVersion: 2, fileName, importable: false, error: "PermissionRejected", plugin, checks: [{ id: "permissions", ok: false, text: "不认识的权限，不能导入。" }] };
  }
  return {
    schema: "workbench.plugin-import.v2.preview", schemaVersion: 2, fileName, importable: true, error: null, plugin,
    checks: [
      { id: "manifest", ok: true, text: "这是一份插件清单。" },
      { id: "permissions", ok: true, text: `允许「${permission.id}」：${permission.reason}。不会联网。` },
      { id: "shape", ok: true, text: "界面会嵌在这个窗口里。没有附带后端程序，所以记下后不会启用。" },
    ],
  };
}

function diagnosticsBody() {
  const plugins = pluginsBody().plugins;
  return {
    schema: "workbench.diagnostics.v2.preview",
    service: { bind: "loopback", lanEnabled: false, autoSend: false },
    plugins: {
      discovered: plugins.length,
      ready: plugins.filter((plugin) => plugin.state === "Ready").length,
      invalid: plugins.filter((plugin) => plugin.state === "Invalid").length,
      loaded: plugins.filter((plugin) => plugin.loaded).length,
    },
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

async function pluginAction(request, url) {
  const project = state.activeProjectId;
  if (request.method === "GET" && url.pathname === `/api/v2/plugins/${upcamId}/actions/draft`) {
    return { status: 200, body: state.digests[project] ?? emptyDigest() };
  }
  if (request.method === "POST" && url.pathname === `/api/v2/plugins/${upcamId}/actions/sample`) {
    const document = upcamSample();
    state.digests[project] = document;
    return { status: 200, body: document };
  }
  if (request.method === "POST" && url.pathname === `/api/v2/plugins/${upcamId}/actions/import`) {
    const result = upcamImport(await jsonBody(request));
    if (result.status === 200) state.digests[project] = result.body;
    return result;
  }
  if (request.method === "GET" && url.pathname === `/api/v2/plugins/${dingId}/actions/state`) {
    return { status: 200, body: state.insights[project] ?? emptyInsights() };
  }
  if (request.method === "POST" && url.pathname === `/api/v2/plugins/${dingId}/actions/sample`) {
    const loaded = dingSample();
    state.chats[project] = loaded;
    const view = insightState(loaded.chat, loaded.source);
    state.insights[project] = view;
    return { status: 200, body: view };
  }
  if (request.method === "POST" && url.pathname === `/api/v2/plugins/${dingId}/actions/import`) {
    const result = dingImport(await jsonBody(request));
    if (result.status !== 200) return result;
    state.chats[project] = result;
    const view = insightState(result.chat, result.source);
    state.insights[project] = view;
    return { status: 200, body: view };
  }
  if (request.method === "POST" && url.pathname === `/api/v2/plugins/${dingId}/actions/analyze`) {
    const stored = state.chats[project];
    if (!stored) return { status: 409, body: { error: "NothingImported" } };
    const view = analyzeChat(stored.chat, stored.source);
    state.insights[project] = view;
    return { status: 200, body: view };
  }
  if (request.method === "POST" && url.pathname === `/api/v2/plugins/${dingId}/actions/send`) {
    return { status: 409, body: sendDisabled() };
  }
  if (request.method === "GET" && url.pathname === `/api/v2/plugins/${testingId}/actions/dashboard`) {
    return { status: 200, body: dashboardDocument() };
  }
  return null;
}

async function jsonBody(request) {
  const text = await readBody(request);
  if (text.length > 262144) return { error: "PayloadTooLarge" };
  try {
    return text ? JSON.parse(text) : {};
  } catch {
    return null;
  }
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
  if (request.method === "POST" && (url.pathname === "/api/v2/plugins/import/check" || url.pathname === "/api/v2/plugins/import")) {
    const body = await jsonBody(request);
    if (!body || typeof body.text !== "string" || typeof body.fileName !== "string") return sendJson(response, 400, { error: "InvalidImport" });
    const report = inspectImport(body);
    if (url.pathname === "/api/v2/plugins/import") {
      if (!report.importable) return sendJson(response, 400, { error: report.error, report });
      state.imports.push({ ...report.plugin, enabled: false, text: body.text, gap: String(report.plugin.id).endsWith("synthetic-gap") });
    }
    return sendJson(response, 200, report);
  }
  const enableMatch = url.pathname.match(/^\/api\/v2\/plugins\/([^/]+)\/enable$/);
  if (request.method === "POST" && enableMatch) {
    const id = decodeURIComponent(enableMatch[1]);
    const item = state.imports.find((plugin) => plugin.id === id);
    if (!item) return sendJson(response, 404, { error: "NotRecorded", message: "这台电脑上没有这份记下的清单，没有启用。" });
    let manifest = null;
    try { manifest = JSON.parse(item.text ?? ""); } catch { manifest = null; }
    if (item.gap || !manifest || manifest.id !== id) {
      item.enabled = false;
      return sendJson(response, 400, { error: "InvalidManifest", message: "没有启用。记下的清单已经不在。" });
    }
    item.enabled = true;
    return sendJson(response, 200, { id, enabled: true });
  }
  if (request.method === "GET" && url.pathname === "/api/v2/plugins") return sendJson(response, 200, pluginsBody());
  if (request.method === "GET" && url.pathname === "/api/v2/diagnostics") return sendJson(response, 200, diagnosticsBody());
  if (request.method === "GET" && url.pathname === "/api/v2/ui/theme") {
    return sendJson(response, 200, { schema: "workbench.ui-theme.v2.preview", schemaVersion: 2, setting: state.themeSetting });
  }
  if (request.method === "GET" && url.pathname === "/api/v2/ui/desk") {
    return sendJson(response, 200, { schema: "workbench.ui-desk.v2.preview", schemaVersion: 2, recovered: false, ...state.desk });
  }
  if (request.method === "PUT" && url.pathname === "/api/v2/ui/desk") {
    const body = JSON.parse(await readBody(request));
    const known = ["upcam", "ding", "test", "runner", ...state.imports.filter((item) => item.enabled).map((item) => item.id)];
    const pinned = Array.isArray(body.pinned) ? body.pinned : null;
    if (!pinned || pinned.length > 6 || pinned.some((id) => !known.includes(id)) || new Set(pinned).size !== pinned.length) {
      return sendJson(response, 400, { error: "InvalidDesk" });
    }
    const touch = body.touch ?? null;
    if (touch !== null && !known.includes(touch)) return sendJson(response, 400, { error: "InvalidDesk" });
    const recent = touch ? [{ id: touch, at: new Date().toISOString() }, ...state.desk.recent.filter((item) => item.id !== touch)].slice(0, 8) : state.desk.recent;
    state.desk = { pinned, recent };
    return sendJson(response, 200, { schema: "workbench.ui-desk.v2.preview", schemaVersion: 2, recovered: false, ...state.desk });
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
  if (request.method === "GET" && url.pathname === "/api/v2/projects/active/context") return sendJson(response, 200, noteBody());
  if (request.method === "PUT" && url.pathname === "/api/v2/projects/active/context") {
    const body = JSON.parse(await readBody(request));
    if (body.projectId && body.projectId !== state.activeProjectId) return sendJson(response, 409, { error: "RevisionConflict" });
    const saved = state.notes[state.activeProjectId] ?? { revision: "missing", text: "" };
    if (body.expectedRevision !== saved.revision) return sendJson(response, 409, { error: "RevisionConflict" });
    const next = { revision: `note-${state.generation + 1}`, text: body.text ?? "" };
    state.notes[state.activeProjectId] = next;
    return sendJson(response, 200, { schemaVersion: 1, revision: next.revision, text: next.text, missing: false });
  }
  if (request.method === "POST" && url.pathname === "/api/v2/projects/name") {
    const body = JSON.parse(await readBody(request));
    if (body.expectedRevision !== state.revision) return sendJson(response, 409, { error: "RevisionConflict" });
    const problem = rejectName(body.name, state.activeProjectId);
    if (problem) return sendJson(response, problem === "DuplicateName" ? 409 : 400, { error: problem });
    const trimmed = body.name.trim();
    const current = state.projects.find((item) => item.id === state.activeProjectId);
    if (current && current.name !== trimmed) {
      current.name = trimmed;
      bumpProject();
    }
    broadcast({ type: "project-changed", activeProjectId: state.activeProjectId });
    return sendJson(response, 200, projectsBody());
  }
  if (request.method === "POST" && url.pathname === "/api/v2/projects") {
    const body = JSON.parse(await readBody(request));
    if (body.expectedRevision !== state.revision) return sendJson(response, 409, { error: "RevisionConflict" });
    const problem = rejectName(body.name);
    if (problem) return sendJson(response, problem === "DuplicateName" ? 409 : 400, { error: problem });
    const id = `p${state.generation}`;
    state.projects = [...state.projects, { id, name: body.name.trim(), isArchived: false }];
    state.notes[id] = { revision: "missing", text: "" };
    state.activeProjectId = id;
    bumpProject();
    broadcast({ type: "project-changed", activeProjectId: state.activeProjectId });
    return sendJson(response, 200, { ...projectsBody(), opened: true });
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
  const pluginResult = await pluginAction(request, url);
  if (pluginResult) return sendJson(response, pluginResult.status, pluginResult.body);
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
  // RFC 6455 section 1.3 requires SHA-1 for the handshake accept value. This is not a password or session hash.
  const accept = createHash("sha1").update(String(key) + magic).digest("base64"); // NOSONAR
  socket.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n\r\n`);
  sockets.add(socket);
  socket.on("data", () => {});
  socket.on("close", () => sockets.delete(socket));
  socket.on("error", () => sockets.delete(socket));
  socket.write(textFrame(JSON.stringify({ type: "ready", deviceId: "local" })));
});

server.listen(port, "127.0.0.1");
