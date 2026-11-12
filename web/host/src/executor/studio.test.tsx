import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";
import { mismatch, recordFromDom, repairPrompt, replay, sampleDom, seedCases } from "./model";
import { ExecutorStudio } from "./studio";

afterEach(() => cleanup());

describe("executor studio", () => {
  it("records text DOM and replays the synthetic sample without calling a model", async () => {
    const user = userEvent.setup();
    render(<ExecutorStudio />);
    expect(screen.getByRole("heading", { name: "自动化测试执行器" })).toBeTruthy();
    expect((screen.getByRole("checkbox", { name: "AI 修复" }) as HTMLInputElement).checked).toBe(false);
    await user.click(screen.getByRole("button", { name: "从样品录制" }));
    expect(screen.getByRole("button", { name: "录制 · 合成电子样品" })).toBeTruthy();
    const objectName = screen.getByLabelText("rec-probe-4 对象");
    await user.clear(objectName);
    await user.type(objectName, "tooth");
    expect((objectName as HTMLInputElement).value).toBe("tooth");
    await user.selectOptions(screen.getByLabelText("rec-probe-4 动作"), "snapshot");
    await user.clear(screen.getByLabelText("rec-probe-4 选择器"));
    await user.type(screen.getByLabelText("rec-probe-4 选择器"), "#view");
    await user.clear(screen.getByLabelText("rec-probe-4 容差"));
    await user.type(screen.getByLabelText("rec-probe-4 容差"), "0.1");
    await user.click(screen.getByRole("button", { name: "回放" }));
    expect(await screen.findByText("全部通过")).toBeTruthy();
    expect(screen.getByText("正常回放不调用模型")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "运行历史" }));
    expect(screen.getByText(/CASE-REC · 通过/)).toBeTruthy();
  });

  it("prepares a text-only repair prompt only after the switch is on", async () => {
    const user = userEvent.setup();
    render(<ExecutorStudio />);
    const expected = screen.getByLabelText("status 期望");
    await user.clear(expected);
    await user.type(expected, "不会出现");
    await user.click(screen.getByRole("checkbox", { name: "AI 修复" }));
    await user.clear(screen.getByLabelText("每日额度"));
    await user.type(screen.getByLabelText("每日额度"), "1");
    await user.click(screen.getByRole("button", { name: "回放" }));
    expect(await screen.findByText("有步骤失败")).toBeTruthy();
    expect(screen.getByText(/失败步骤 status/)).toBeTruthy();
    expect(screen.getByText("已准备文本提示，模型调用 0 次")).toBeTruthy();
  });
});

describe("executor script", () => {
  it("keeps screenshots out of the recorded script and diffs pixels locally", () => {
    const steps = recordFromDom(sampleDom);
    expect(steps.some((item) => item.action === "click" && item.selector === "#start")).toBe(true);
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
  });
});
