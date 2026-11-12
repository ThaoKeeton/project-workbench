export type StepAction =
  | "attach"
  | "click"
  | "click-scene"
  | "type"
  | "wait-for"
  | "assert-text"
  | "assert-attr"
  | "assert-number"
  | "screenshot";

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
      recorded.push(step(`rec-scene-${index + 1}`, "click-scene", selector || "#view", objectName, "camera.y=4±0.2", "0.2"));
      recorded.push(step(`rec-number-${index + 1}`, "assert-number", selector || "#view", "camera.y", "4", "0.2"));
    }
  });
  recorded.push(step("rec-shot", "screenshot", "#view", "", "", "0"));
  return recorded;
}

export function repairPrompt(dom: string, failed: ExecutorStep): string {
  return ["只使用下面的文本结构修复失败步骤。不要附带截图。", "DOM:", dom, `失败步骤 ${failed.id} ${failed.action} ${failed.selector} 期望 ${failed.expected}`].join("\n");
}

export function replay(testCase: ExecutorCase, dom: string): ExecutorRun {
  let status = "";
  let attr = "data-state=";
  const scene = { y: 4, size: 2 };
  const frame = new Uint8Array([9, 8, 7, 255]);
  const steps = testCase.steps.map((item) => {
    const started = Date.now();
    let ok = true;
    let detail = "ok";
    if (item.action === "attach") ok = item.expected.length === 0 || dom.includes("开始");
    if (item.action === "click" && (item.selector === "#start" || item.text === "开始")) status = "已开始";
    if (item.action === "click-scene") scene.y = 4;
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
      const tolerance = Number(item.tolerance || "0");
      const expected = Number(item.expected);
      const actual = item.text === "mesh.cube.size" ? scene.size : scene.y;
      ok = Math.abs(actual - expected) <= tolerance;
      detail = String(actual);
    }
    if (item.action === "screenshot") {
      const ratio = mismatch(frame, frame, Number(item.tolerance || "0"));
      ok = ratio === 0;
      detail = `diff=${ratio}`;
    }
    return { id: item.id, status: ok ? "passed" as const : "failed" as const, ms: Math.max(1, Date.now() - started), detail, evidence: item.action === "screenshot" ? "local:rgba(9,8,7,255)" : "" };
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
