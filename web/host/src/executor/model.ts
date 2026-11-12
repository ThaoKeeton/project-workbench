export type StepAction =
  | "attach"
  | "click"
  | "click-scene"
  | "drag-scene"
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

const sceneActions = new Set<StepAction>(["click-scene", "drag-scene", "wheel-scene", "probe-scene", "snapshot", "diff-scene", "assert-mesh", "compare-pixels"]);

export function isSceneAction(action: StepAction): boolean {
  return sceneActions.has(action);
}

export const sceneSnapshotText = [
  "library=three mount=devtools",
  "Scene/cube type=Mesh uuid=cube-1 visible=1 pos=(0, 0, 0) rot=(0, 0, 0) verts=24 faces=12 vol=8 hash=box2",
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
  "[canvas] name=view text= selector=#view object=cube",
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

export function repairPrompt(dom: string, failed: ExecutorStep): string {
  const scene = isSceneAction(failed.action) ? `SCENE:\n${sceneSnapshotText}\n` : "";
  return [`${scene}只使用下面的文字结构修复失败步骤。不要附带截图。`, "DOM:", dom, `失败步骤 ${failed.id} ${failed.action} ${failed.selector} 期望 ${failed.expected}`].join("\n");
}

type SceneState = { y: number; size: number; x: number; snapped: boolean };

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
  if (item.action === "assert-mesh") {
    return numberOutcome(item, item.text === "position.x" ? scene.x : scene.y);
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

export function replay(testCase: ExecutorCase, dom: string): ExecutorRun {
  let status = "";
  let attr = "data-state=";
  const scene: SceneState = { y: 4, size: 2, x: 0, snapped: false };
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
