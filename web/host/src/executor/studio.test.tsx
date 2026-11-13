import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";
import { authorPrompt, caseFromScript, connectChoice, describeStep, fixedReplayScript, mismatch, readAuthoredScript, recordFromDom, repairPrompt, replay, resultSentence, sampleAppName, sampleDom, sceneSnapshotText, seedCases } from "./model";
import { ExecutorStudio } from "./studio";

afterEach(() => {
  cleanup();
  sessionStorage.clear();
});

async function reachDraft(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: "选择程序" }));
  await user.click(screen.getByRole("button", { name: sampleAppName }));
  expect(screen.getByRole("status").textContent).toContain("已连接");
  await user.click(screen.getByRole("button", { name: "下一步" }));
  await user.type(screen.getByLabelText("要测什么"), "切削工件后，工件变小，旁边的牙不变");
  await user.click(screen.getByRole("button", { name: "下一步" }));
}

describe("executor wizard", () => {
  it("starts empty, connects the sample, and explains a missing key", async () => {
    const user = userEvent.setup();
    render(<ExecutorStudio />);
    expect(screen.getByRole("heading", { name: "还没有开始" })).toBeTruthy();
    expect(screen.getByText(/先选择要测的程序/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "选择程序" }));
    const file = new File(["x"], "notes.txt", { type: "text/plain" });
    fireEvent.change(screen.getByLabelText("浏览程序"), { target: { files: [file] } });
    expect(screen.getByRole("status").textContent).toContain("没连上");
    expect(screen.getByRole("status").textContent).toContain("请重新选择程序文件");
    await user.click(screen.getByRole("button", { name: "重新选择" }));
    await user.click(screen.getByRole("button", { name: sampleAppName }));
    expect(screen.getByRole("status").textContent).toContain("已连接");
    await user.click(screen.getByRole("button", { name: "下一步" }));
    expect(screen.getByRole("button", { name: "下一步" }).hasAttribute("disabled")).toBe(true);
    await user.type(screen.getByLabelText("要测什么"), "切削工件后，工件变小，旁边的牙不变");
    await user.click(screen.getByRole("button", { name: "下一步" }));
    expect(screen.getByText(/需要先设置 AI 密钥/)).toBeTruthy();
    expect(screen.getByRole("link", { name: "去设置" }).getAttribute("href")).toBe("/settings");
    expect(screen.getByLabelText("每天最多问几次")).toBeTruthy();
    expect(screen.getByText(/这里不写金额/)).toBeTruthy();
    expect(screen.queryByText(/uuid=/)).toBeNull();
  });

  it("drafts a plain checklist, demos it, then saves a daily rerun", async () => {
    const user = userEvent.setup();
    let calls = 0;
    render(<ExecutorStudio complete={async () => {
      calls += 1;
      return { text: fixedReplayScript, cost: 0 };
    }} />);
    await reachDraft(user);
    expect(await screen.findByText("看一遍下面要做的事。看着演示确认后，再保存。")).toBeTruthy();
    expect(screen.getByText("点击「开始」")).toBeTruthy();
    expect(screen.getByText("沿着路径切一刀")).toBeTruthy();
    expect(screen.getByText("工件变小，旁边的牙保持原样")).toBeTruthy();
    expect(calls).toBe(1);
    await user.click(screen.getByText("详情"));
    expect(screen.getByText(/workbench.test-executor.authored-script.v1/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "演示一遍" }));
    expect(await screen.findByText("通过")).toBeTruthy();
    expect(screen.getByText(resultSentence(true))).toBeTruthy();
    expect(screen.getByRole("img", { name: "之前" })).toBeTruthy();
    expect(screen.getByRole("img", { name: "之后" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "保存" }));
    expect(screen.getByText(/已留下/)).toBeTruthy();
    fireEvent.change(screen.getByLabelText("每天重跑的时间"), { target: { value: "07:30" } });
    await user.click(screen.getByRole("button", { name: "保存安排" }));
    expect(screen.getByRole("status").textContent).toContain("到 07:30 会再跑一遍");
    expect(screen.getByRole("status").textContent).toContain("不会再问");
  });

  it("stops when the daily count is used up and rejects a bad draft", async () => {
    const user = userEvent.setup();
    const { unmount } = render(<ExecutorStudio complete={async () => ({ text: fixedReplayScript, cost: 1 })} />);
    await reachDraft(user);
    fireEvent.change(screen.getByLabelText("每天最多问几次"), { target: { value: "0" } });
    await user.click(screen.getByRole("button", { name: "再问一次" }));
    expect(screen.getByText(authorOver())).toBeTruthy();
    unmount();

    let text = "not-json";
    render(<ExecutorStudio complete={async () => ({ text, cost: 0 })} />);
    await reachDraft(user);
    expect(await screen.findByText("这次没有写成可以执行的检查。请改一句说明，再试一次。")).toBeTruthy();
    text = fixedReplayScript;
    await user.click(screen.getByRole("button", { name: "再问一次" }));
    expect(await screen.findByRole("button", { name: "演示一遍" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "演示一遍" }));
    await user.click(screen.getByRole("button", { name: "返回" }));
    expect(screen.getByRole("heading", { name: "看它准备做什么" })).toBeTruthy();
  });

  it("uses the fixed local draft when the preview flag is set", async () => {
    sessionStorage.setItem("workbench.author.fake", "1");
    const user = userEvent.setup();
    render(<ExecutorStudio />);
    await reachDraft(user);
    expect(await screen.findByText("沿着路径切一刀")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "演示一遍" }));
    expect(screen.getByText("通过")).toBeTruthy();
  });
});

function authorOver(): string {
  return "今天已经问得够多了。先用保存好的检查，或把每天的次数调大。";
}

describe("executor script", () => {
  it("keeps screenshots out of the recorded script and diffs pixels locally", () => {
    const steps = recordFromDom(sampleDom);
    expect(steps.some((item) => item.action === "click" && item.selector === "#start")).toBe(true);
    expect(steps.some((item) => item.action === "drag-path" && item.text === "stock" && item.expected === "4")).toBe(true);
    expect(JSON.stringify(steps)).not.toContain("data:image");
    const result = replay({ ...seedCases()[0], steps }, sampleDom);
    expect(result.passed).toBe(true);
    expect(result.steps.find((item) => item.id === "rec-shot")?.evidence.startsWith("local:")).toBe(true);
    expect(mismatch(new Uint8Array([1, 2]), new Uint8Array([1, 9]), 0)).toBeGreaterThan(0);
    expect(repairPrompt(sampleDom, steps[1])).toContain("DOM:");
    expect(repairPrompt(sampleDom, steps[1])).not.toContain("data:image");
    const sceneStep = steps.find((item) => item.action === "probe-scene");
    expect(sceneStep).toBeTruthy();
    const prompt = repairPrompt(sampleDom, sceneStep!);
    expect(prompt).toContain("SCENE:");
    expect(prompt).toContain("hash=box2");
    expect(prompt).not.toContain("data:image");
    expect(result.steps.find((item) => item.id.startsWith("rec-pixels"))?.evidence.startsWith("local:")).toBe(true);
    const authored = authorPrompt("切削工件后体积减小，邻件不变", sampleDom, sceneSnapshotText);
    expect(authored).toContain("schemaVersion 必须是 1");
    expect(authored).toContain("不要要求修改目标应用");
    expect(authored).not.toContain("data:image");
    expect(readAuthoredScript('{"schema":"workbench.test-executor.authored-script.v1","schemaVersion":2,"id":"A","name":"n","steps":[{"id":"s","type":"web","action":"click"}]}')).toBeNull();
    const saved = caseFromScript(fixedReplayScript);
    expect(saved?.script).toBe(fixedReplayScript);
    expect(replay(saved!, sampleDom).passed).toBe(true);
    expect(connectChoice(sampleAppName).status).toBe("已连接");
    expect(connectChoice("Cutter.exe").ok).toBe(true);
    expect(connectChoice("notes.txt").ok).toBe(false);
    expect(describeStep(saved!.steps[1]).will).toContain("点击");
    expect(resultSentence(false)).toContain("再演示一遍");
  });
});
