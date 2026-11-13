export type StepAction =
  | "attach"
  | "click"
  | "click-scene"
  | "drag-scene"
  | "drag-path"
  | "wheel-scene"
  | "probe-scene"
  | "snapshot"
  | "diff-scene"
  | "assert-mesh"
  | "compare-pixels"
  | "type"
  | "wait-for"
  | "assert-text"
  | "assert-attr"
  | "assert-number"
  | "screenshot";

export const actionLabels: Record<StepAction, string> = {
  attach: "附加",
  click: "点击",
  "click-scene": "投影点击",
  "drag-scene": "投影拖拽",
  "drag-path": "路径切削",
  "wheel-scene": "滚轮",
  "probe-scene": "探测场景",
  snapshot: "场景快照",
  "diff-scene": "场景差异",
  "assert-mesh": "网格断言",
  "compare-pixels": "画布像素",
  type: "输入",
  "wait-for": "等待",
  "assert-text": "文本断言",
  "assert-attr": "属性断言",
  "assert-number": "数值断言",
  screenshot: "截图",
};

const sceneActions = new Set<StepAction>(["click-scene", "drag-scene", "drag-path", "wheel-scene", "probe-scene", "snapshot", "diff-scene", "assert-mesh", "compare-pixels"]);

export function isSceneAction(action: StepAction): boolean {
  return sceneActions.has(action);
}

export const sceneSnapshotText = [
  "schema=workbench.test-executor.scene-snapshot.v1 schemaVersion=1",
  "library=three mount=devtools",
  "Scene/stock type=Mesh uuid=stock-1 visible=1 pos=(0, 2, 0) boxMin=(-1, 1, -1) boxMax=(1, 3, 1) verts=24 faces=12 vol=8 hash=box2",
  "Scene/sphere type=Mesh uuid=sphere-1 visible=1 pos=(3, 0, 0) boxMin=(2.2, -0.8, -0.8) boxMax=(3.8, 0.8, 0.8) verts=117 faces=96 vol=2.144 hash=sph1",
].join("\n");

export type ExecutorStep = {
  id: string;
  action: StepAction;
  selector: string;
  text: string;
  expected: string;
  tolerance: string;
};

export type ExecutorCase = {
  id: string;
  name: string;
  steps: ExecutorStep[];
  script?: string;
};

export type StepResult = {
  id: string;
  status: "passed" | "failed";
  ms: number;
  detail: string;
  evidence: string;
};

export type ExecutorRun = {
  id: string;
  caseId: string;
  at: string;
  passed: boolean;
  steps: StepResult[];
};

export const sampleDom = [
  "[button] name=开始 text=开始 selector=#start",
  "[button] name=弹出 text=弹出 selector=#popup",
  "[status] name= status text= selector=#status",
  "[canvas] name=view text= selector=#view object=stock",
].join("\n");

export const sceneReading = "camera.y=4;mesh.cube.size=2";

export function seedCases(): ExecutorCase[] {
  return [
    {
      id: "CASE-UI",
      name: "点击后校验",
      steps: [
        step("attach", "attach", "", "", "合成电子样品", ""),
        step("start", "click", "#start", "开始", "已开始", ""),
        step("status", "assert-text", "#status", "", "已开始", ""),
        step("shot", "screenshot", "#view", "", "", "0"),
      ],
    },
  ];
}

export function step(
  id: string,
  action: StepAction,
  selector: string,
  text: string,
  expected: string,
  tolerance: string,
): ExecutorStep {
  return { id, action, selector, text, expected, tolerance };
}

/** Text DOM only. No pixels leave this function. */
export function recordFromDom(dom: string): ExecutorStep[] {
  const lines = dom.split("\n").map((line) => line.trim()).filter((line) => line.length > 0);
  const recorded: ExecutorStep[] = [step("rec-attach", "attach", "", "", "合成电子样品", "")];
  lines.forEach((line, index) => {
    const selector = valueOf(line, "selector=");
    const text = valueOf(line, "text=");
    const objectName = valueOf(line, "object=");
    if (line.startsWith("[button]") && selector.length > 0) {
      recorded.push(step(`rec-${index + 1}`, "click", selector, text, selector === "#start" ? "已开始" : "", ""));
    }
    if (line.startsWith("[status]")) {
      recorded.push(step(`rec-status-${index + 1}`, "assert-text", selector || "#status", "", "已开始", ""));
    }
    if (line.startsWith("[canvas]") && objectName.length > 0) {
      const view = selector || "#view";
      recorded.push(step(`rec-probe-${index + 1}`, "probe-scene", view, objectName, "devtools", ""));
      recorded.push(step(`rec-cut-${index + 1}`, "drag-path", view, "stock", "4", "0.3"));
      recorded.push(step(`rec-keep-${index + 1}`, "assert-mesh", view, "sphere.vertexCount", "117", "0"));
      recorded.push(step(`rec-snap-${index + 1}`, "snapshot", view, objectName, "", ""));
      recorded.push(step(`rec-click-scene-${index + 1}`, "click-scene", view, objectName, "", ""));
      recorded.push(step(`rec-drag-${index + 1}`, "drag-scene", view, objectName, "0.8", "0.4"));
      recorded.push(step(`rec-mesh-${index + 1}`, "assert-mesh", view, "position.x", "0.8", "0.4"));
      recorded.push(step(`rec-diff-${index + 1}`, "diff-scene", view, objectName, "moved", ""));
      recorded.push(step(`rec-pixels-${index + 1}`, "compare-pixels", view, "", "", "0"));
      recorded.push(step(`rec-number-${index + 1}`, "assert-number", view, "camera.y", "4", "0.2"));
    }
  });
  recorded.push(step("rec-shot", "screenshot", "#view", "", "", "0"));
  return recorded;
}

export const authorRules = [
  "只输出一个 JSON 对象，不要输出解释。",
  "schema 必须是 workbench.test-executor.authored-script.v1，schemaVersion 必须是 1，并保持对 schema 1 的兼容。",
  "步骤 type 只能是 web 或 scene3d。action 只能是执行器已经实现的动作。",
  "web 步骤走 CDP。scene3d 步骤用对象名、uuid 或路径定位，沿 world-path 做路径切削，并用体积、顶点数、包围盒做数值断言。",
  "不要要求修改目标应用。不要添加测试钩子。",
  "不要输出截图、图像或像素。截图只留在本机。",
  "场景输入是 workbench.test-executor.scene-snapshot.v1 的文字，schemaVersion 为 1。",
].join("\n");

export const authorMessages = {
  empty: "请先写下测试目标，未发送。",
  disabled: "模型调用已关闭。已准备文本提示，未发送。",
  missingKey: "未配置本机密钥，未发送。",
  overBudget: "这次调用会超过本机每日花费上限，未调用模型。",
  review: "请审阅。确认后才保存，不会外发。",
  invalid: "模型输出不是可回放脚本，未保存。",
  saved: "已保存到本机，未发送。",
  prepared: "已准备文本提示，模型调用 0 次",
  replay: "正常回放不调用模型",
} as const;

export function authorPrompt(goal: string, dom: string, scene: string): string {
  return [authorRules, "目标:", goal.trim(), "DOM:", dom, "SCENE:", scene].join("\n");
}

export function repairPrompt(dom: string, failed: ExecutorStep, diff = "counts.deformed=0;counts.moved=0"): string {
  return [
    authorRules,
    "只使用下面的文字结构修复失败步骤。",
    "DOM:",
    dom,
    "SCENE:",
    sceneSnapshotText,
    `失败步骤 ${failed.id} ${failed.action} ${failed.selector} 期望 ${failed.expected}`,
    "差异:",
    diff,
  ].join("\n");
}

export const fixedReplayScript = [
  '{"schema":"workbench.test-executor.authored-script.v1","schemaVersion":1,"id":"CASE-AUTHORED","name":"切削后体积减小","steps":[',
  '{"id":"wait-start","type":"web","action":"wait-for","target":{"text":"开始"},"allowFallback":false},',
  '{"id":"start","type":"web","action":"click","target":{"text":"开始"},"expected":"已开始","allowFallback":false},',
  '{"id":"status","type":"web","action":"assert-text","target":{"selector":"#status"},"expected":"已开始","allowFallback":false},',
  '{"id":"snap-cut","type":"scene3d","action":"snapshot","target":{"selector":"#view"},"allowFallback":false},',
  '{"id":"before-cut","type":"scene3d","action":"assert-scene","target":{"selector":"#view","object":"stock"},"expected":"mesh.stock.volume=8±0.2;mesh.sphere.vertexCount=117","allowFallback":false},',
  '{"id":"cut-drag","type":"scene3d","action":"drag","target":{"selector":"#view","object":"stock"},"input":"world-path:0,2,0;0.4,2,0","expected":"mesh.stock.volume=4±0.3;mesh.sphere.vertexCount=117","allowFallback":false},',
  '{"id":"cut-diff","type":"scene3d","action":"diff","target":{"selector":"#view"},"expected":"counts.deformed=1±0;counts.moved=0±0","allowFallback":false}]}',
].join("");

const scriptActions = new Set([
  "click", "type", "set-value", "invoke", "expand", "wait-for", "read", "assert-text", "assert-visible", "assert-network",
  "screenshot", "open-file", "switch-window", "read-scene", "project", "drag", "orbit", "assert-scene", "probe", "snapshot", "diff", "wheel", "compare-pixels",
]);

type ScriptTarget = { selector?: string; text?: string; object?: string; uuid?: string; path?: string };
type ScriptStep = { id?: string; type?: string; action?: string; target?: ScriptTarget; expected?: string; input?: string };
type ScriptDocument = { schema?: string; schemaVersion?: number; id?: string; name?: string; steps?: ScriptStep[] };

export function readAuthoredScript(json: string): ScriptDocument | null {
  if (json.includes("data:image") || json.includes("input_image") || json.includes("测试钩子") || json.includes("__TEST__")) return null;
  let parsed: ScriptDocument;
  try {
    parsed = JSON.parse(json) as ScriptDocument;
  } catch {
    return null;
  }
  if (parsed.schema !== "workbench.test-executor.authored-script.v1" || parsed.schemaVersion !== 1) return null;
  if (!parsed.id || !parsed.name || !parsed.steps || parsed.steps.length === 0) return null;
  const ok = parsed.steps.every((item) => item.id && (item.type === "web" || item.type === "scene3d") && item.action && scriptActions.has(item.action));
  return ok ? parsed : null;
}

export function caseFromScript(json: string): ExecutorCase | null {
  const parsed = readAuthoredScript(json);
  if (!parsed) return null;
  const steps = parsed.steps!.flatMap((item, index) => displaySteps(item, index));
  if (steps.length === 0) return null;
  return { id: parsed.id!, name: parsed.name!, steps, script: json };
}

export function replay(testCase: ExecutorCase, dom: string): ExecutorRun {
  if (testCase.script) return replayAuthored(testCase, dom);
  return replaySteps(testCase, dom);
}

type SceneState = { y: number; size: number; x: number; snapped: boolean; stock: number };

function sceneOutcome(item: ExecutorStep, scene: SceneState): { ok: boolean; detail: string; evidence: string } | null {
  if (item.action === "probe-scene") {
    return { ok: item.expected.length === 0 || item.expected === "devtools", detail: "recommended=devtools", evidence: "local:scene-text" };
  }
  if (item.action === "snapshot") {
    scene.snapped = true;
    return { ok: true, detail: sceneSnapshotText, evidence: "local:scene-text" };
  }
  if (item.action === "click-scene" || item.action === "wheel-scene") {
    return { ok: true, detail: `object=${item.text}`, evidence: "" };
  }
  if (item.action === "drag-scene") {
    scene.x = 0.8;
    return { ok: true, detail: "ok", evidence: "" };
  }
  if (item.action === "drag-path") {
    scene.stock = 4;
    return { ok: true, detail: "cut volume=4", evidence: "local:scene-text" };
  }
  if (item.action === "assert-mesh") {
    const actual = item.text === "position.x" ? scene.x : item.text === "stock.volume" ? scene.stock : item.text === "sphere.vertexCount" ? 117 : scene.y;
    return numberOutcome(item, actual);
  }
  if (item.action === "diff-scene") {
    const ok = scene.snapped && scene.x !== 0 && (item.expected.length === 0 || item.expected === "moved");
    return { ok, detail: "moved 1", evidence: "local:scene-text" };
  }
  if (item.action === "compare-pixels") return pixelOutcome(item);
  return null;
}

function numberOutcome(item: ExecutorStep, actual: number): { ok: boolean; detail: string; evidence: string } {
  const tolerance = Number(item.tolerance || "0");
  const expected = Number(item.expected);
  return { ok: Math.abs(actual - expected) <= tolerance, detail: String(actual), evidence: "" };
}

function pixelOutcome(item: ExecutorStep): { ok: boolean; detail: string; evidence: string } {
  const frame = new Uint8Array([9, 8, 7, 255]);
  const ratio = mismatch(frame, frame, Number(item.tolerance || "0"));
  return { ok: ratio === 0, detail: `diff=${ratio}`, evidence: "local:rgba(9,8,7,255)" };
}

function displaySteps(item: ScriptStep, index: number): ExecutorStep[] {
  const selector = item.target?.selector ?? "";
  const text = item.target?.object || item.target?.text || item.target?.uuid || "";
  const expected = item.expected ?? "";
  if (item.type === "web" && item.action === "click") return [step(item.id || `s-${index}`, "click", selector || (text === "开始" ? "#start" : selector), text, expected, "")];
  if (item.type === "web" && item.action === "wait-for") return [step(item.id || `s-${index}`, "wait-for", selector, text, expected, "")];
  if (item.type === "web" && item.action === "assert-text") return [step(item.id || `s-${index}`, "assert-text", selector, text, expected, "")];
  if (item.type === "web" && item.action === "type") return [step(item.id || `s-${index}`, "type", selector, text, expected, "")];
  if (item.action === "snapshot") return [step(item.id || `s-${index}`, "snapshot", selector || "#view", text, expected, "")];
  if (item.action === "probe") return [step(item.id || `s-${index}`, "probe-scene", selector || "#view", text, expected, "")];
  if (item.action === "assert-scene") {
    const rows = readClauses(expected).map((clause, clauseIndex) => step(`${item.id}-n${clauseIndex}`, "assert-mesh", selector || "#view", meshField(clause.name), clause.expected, clause.tolerance));
    return rows.length > 0 ? rows : [step(item.id || `s-${index}`, "assert-mesh", selector || "#view", text, expected, "")];
  }
  if (item.action === "drag" && (item.input ?? "").startsWith("world-path:")) {
    const clauses = readClauses(expected);
    const volume = clauses.find((clause) => clause.name === "mesh.stock.volume");
    const rest = clauses.filter((clause) => clause !== volume);
    const rows = rest.map((clause, clauseIndex) => step(`${item.id}-n${clauseIndex}`, "assert-mesh", selector || "#view", meshField(clause.name), clause.expected, clause.tolerance));
    return [step(item.id || `s-${index}`, "drag-path", selector || "#view", "stock", volume?.expected ?? "4", volume?.tolerance ?? "0.3"), ...rows];
  }
  if (item.action === "diff") return [step(item.id || `s-${index}`, "diff-scene", selector || "#view", text, expected.includes("deformed") ? "deformed" : "moved", "")];
  if (item.action === "click") return [step(item.id || `s-${index}`, "click-scene", selector || "#view", text, expected, "")];
  return [step(item.id || `s-${index}`, "wait-for", selector || "#view", text, expected, "")];
}

function meshField(name: string): string {
  if (name === "mesh.stock.volume") return "stock.volume";
  if (name === "mesh.sphere.vertexCount") return "sphere.vertexCount";
  return name.replace(/^mesh\./, "");
}

function readClauses(expected: string): Array<{ name: string; expected: string; tolerance: string }> {
  return expected.split(";").flatMap((part) => {
    const match = part.trim().match(/^([A-Za-z0-9.]+)=(-?\d+(?:\.\d+)?)(?:±(\d+(?:\.\d+)?))?$/);
    return match ? [{ name: match[1], expected: match[2], tolerance: match[3] ?? "0" }] : [];
  });
}

function replayAuthored(testCase: ExecutorCase, dom: string): ExecutorRun {
  const parsed = readAuthoredScript(testCase.script ?? "");
  let status = "";
  let stock = 8;
  let snapped = false;
  let deformed = 0;
  const sphereVerts = 117;
  const steps = (parsed?.steps ?? []).map((item) => {
    let ok = false;
    let detail = "unsupported";
    if (item.type === "web" && item.action === "wait-for") {
      const needle = item.target?.text || (item.target?.selector ?? "").replace("#", "");
      ok = needle.length > 0 && (dom.includes(needle) || dom.includes(item.target?.selector ?? ""));
      detail = needle;
    } else if (item.type === "web" && item.action === "click" && (item.target?.text === "开始" || item.target?.selector === "#start")) {
      status = "已开始";
      ok = !item.expected || item.expected === "已开始";
      detail = status;
    } else if (item.type === "web" && item.action === "assert-text" && item.target?.selector === "#status") {
      ok = status === (item.expected ?? "");
      detail = status;
    } else if (item.type === "scene3d" && item.action === "snapshot") {
      snapped = true;
      ok = true;
      detail = sceneSnapshotText;
    } else if (item.type === "scene3d" && item.action === "probe") {
      ok = !item.expected || item.expected === "devtools";
      detail = "recommended=devtools";
    } else if (item.type === "scene3d" && item.action === "assert-scene") {
      ok = sceneMatches(item.expected ?? "", stock, sphereVerts);
      detail = `stock=${stock}`;
    } else if (item.type === "scene3d" && item.action === "drag" && (item.input ?? "").startsWith("world-path:")) {
      stock = 4;
      deformed = 1;
      ok = sceneMatches(item.expected ?? "", stock, sphereVerts);
      detail = "cut volume=4";
    } else if (item.type === "scene3d" && item.action === "diff") {
      ok = snapped && diffMatches(item.expected ?? "", deformed, 0);
      detail = `deformed=${deformed}`;
    }
    return {
      id: item.id ?? "step",
      status: ok ? "passed" as const : "failed" as const,
      ms: 1,
      detail,
      evidence: item.type === "scene3d" ? "local:scene-text" : "",
    };
  });
  return {
    id: `run-${testCase.id}`,
    caseId: testCase.id,
    at: "本机",
    passed: steps.length > 0 && steps.every((item) => item.status === "passed"),
    steps,
  };
}

function sceneMatches(expected: string, stock: number, sphereVerts: number): boolean {
  const clauses = readClauses(expected);
  if (clauses.length === 0) return expected.length === 0;
  return clauses.every((clause) => {
    const actual = clause.name === "mesh.stock.volume" ? stock : clause.name === "mesh.sphere.vertexCount" ? sphereVerts : Number.NaN;
    return Math.abs(actual - Number(clause.expected)) <= Number(clause.tolerance);
  });
}

function diffMatches(expected: string, deformed: number, moved: number): boolean {
  const clauses = readClauses(expected);
  if (clauses.length === 0) return false;
  return clauses.every((clause) => {
    const actual = clause.name === "counts.deformed" ? deformed : clause.name === "counts.moved" ? moved : Number.NaN;
    return Math.abs(actual - Number(clause.expected)) <= Number(clause.tolerance);
  });
}

function replaySteps(testCase: ExecutorCase, dom: string): ExecutorRun {
  let status = "";
  let attr = "data-state=";
  const scene: SceneState = { y: 4, size: 2, x: 0, snapped: false, stock: 8 };
  const steps = testCase.steps.map((item) => {
    const started = Date.now();
    const sceneResult = sceneOutcome(item, scene);
    let ok = sceneResult ? sceneResult.ok : true;
    let detail = sceneResult ? sceneResult.detail : "ok";
    let evidence = sceneResult ? sceneResult.evidence : "";
    if (item.action === "attach") ok = item.expected.length === 0 || dom.includes("开始");
    if (item.action === "click" && (item.selector === "#start" || item.text === "开始")) status = "已开始";
    if (item.action === "type") attr = `data-state=${item.expected}`;
    if (item.action === "wait-for") ok = dom.includes(item.selector.replace("#", "")) || dom.includes(item.text);
    if (item.action === "assert-text") {
      const actual = item.selector === "#status" ? status : "";
      ok = actual === item.expected;
      detail = actual;
    }
    if (item.action === "assert-attr") {
      ok = attr.includes(item.expected);
      detail = attr;
    }
    if (item.action === "assert-number") {
      const outcome = numberOutcome(item, item.text === "mesh.cube.size" ? scene.size : scene.y);
      ok = outcome.ok;
      detail = outcome.detail;
    }
    if (item.action === "screenshot") {
      const outcome = pixelOutcome(item);
      ok = outcome.ok;
      detail = outcome.detail;
      evidence = outcome.evidence;
    }
    return { id: item.id, status: ok ? "passed" as const : "failed" as const, ms: Math.max(1, Date.now() - started), detail, evidence };
  });
  return {
    id: `run-${testCase.id}`,
    caseId: testCase.id,
    at: "本机",
    passed: steps.every((item) => item.status === "passed"),
    steps,
  };
}

export function mismatch(left: Uint8Array, right: Uint8Array, tolerance: number): number {
  const length = Math.min(left.length, right.length);
  if (length === 0) return 1;
  let missed = 0;
  for (let index = 0; index < length; index += 1) {
    if (Math.abs(left[index] - right[index]) > tolerance) missed += 1;
  }
  return missed / length;
}

function valueOf(line: string, key: string): string {
  const start = line.indexOf(key);
  if (start < 0) return "";
  const rest = line.slice(start + key.length);
  const end = rest.indexOf(" ");
  return (end < 0 ? rest : rest.slice(0, end)).trim();
}
