import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";
import { authorPrompt, capStorageKey, caseFromScript, connectChoice, describeStep, failingReplayScript, fixedReplayScript, formatWhen, humanFindings, mismatch, nextDailyInstant, plainPlan, readAuthoredScript, readDailyCap, recordFromDom, repairPrompt, replay, resultSentence, sampleAppName, sampleDom, sampleLibrary, sceneSnapshotText, seedCases, writeDailyCap } from "./model";
import { ExecutorStudio } from "./studio";

afterEach(() => {
  cleanup();
  sessionStorage.clear();
  localStorage.removeItem(capStorageKey);
});

async function openSample(user: ReturnType<typeof userEvent.setup>, start = "选择程序") {
  await user.click(screen.getByRole("button", { name: start }));
  await user.click(screen.getByRole("button", { name: sampleAppName }));
  expect(screen.getByRole("status").textContent).toContain("已连接");
  await user.click(screen.getByRole("button", { name: "下一步" }));
  await user.type(screen.getByLabelText("要测什么"), "切削工件后，工件变小，旁边的牙不变");
  await user.click(screen.getByRole("button", { name: "下一步" }));
}

describe("executor wizard", () => {
  it("starts empty, connects the sample, and explains a missing key", async () => {
    const user = userEvent.setup();
    render(<ExecutorStudio stepDelay={10000} />);
    expect(screen.getByRole("heading", { name: "还没有测试" })).toBeTruthy();
    const file = new File(["x"], "notes.txt", { type: "text/plain" });
    await user.click(screen.getByRole("button", { name: "选择程序" }));
    fireEvent.change(screen.getByLabelText("浏览程序"), { target: { files: [file] } });
    expect(screen.getByRole("status").textContent).toContain("没连上");
    await user.click(screen.getByRole("button", { name: "重新选择" }));
    await user.click(screen.getByRole("button", { name: sampleAppName }));
    expect(screen.getByRole("status").textContent).toContain("已连接");
    await user.click(screen.getByRole("button", { name: "下一步" }));
    expect((screen.getByRole("button", { name: "下一步" }) as HTMLButtonElement).disabled).toBe(true);
    await user.type(screen.getByLabelText("要测什么"), "切削工件后，工件变小，旁边的牙不变");
    await user.click(screen.getByRole("button", { name: "下一步" }));
    expect(screen.getByText(/需要先设置 AI 密钥/)).toBeTruthy();
    expect(screen.getByRole("link", { name: "去设置" }).getAttribute("href")).toBe("/settings");
    expect(screen.getByText(/今天还能生成/)).toBeTruthy();
    expect(screen.queryByLabelText("每天最多问几次")).toBeNull();
  });

  it("shows a short checklist, a live demo, and a saved home", async () => {
    const user = userEvent.setup();
    let calls = 0;
    render(<ExecutorStudio stepDelay={10000} complete={async () => {
      calls += 1;
      return { text: fixedReplayScript, cost: 0 };
    }} />);
    await openSample(user);
    expect(screen.getByRole("heading", { name: "它会做" })).toBeTruthy();
    expect(screen.getByRole("heading", { name: "它会核对" })).toBeTruthy();
    expect(screen.getAllByText("旁边的牙保持原样")).toHaveLength(1);
    expect(screen.getByText("沿着路径切一刀")).toBeTruthy();
    expect(screen.getByText("工件变小")).toBeTruthy();
    expect(calls).toBe(1);
    await user.type(screen.getByLabelText("哪里不对"), "牙不要变");
    await user.click(screen.getByRole("button", { name: "重新生成" }));
    expect(calls).toBe(2);
    await user.click(screen.getByRole("button", { name: "演示一遍" }));
    expect(screen.getByRole("button", { name: "停止" })).toBeTruthy();
    expect(screen.getByText("正在检查下一项。")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "停止" }));
    expect(screen.getByText(/已停下/)).toBeTruthy();
  });

  it("finishes a passing demo and returns home with the saved test", async () => {
    const user = userEvent.setup();
    render(<ExecutorStudio stepDelay={0} complete={async () => ({ text: fixedReplayScript, cost: 0 })} />);
    await openSample(user);
    await user.click(screen.getByRole("button", { name: "演示一遍" }));
    expect(await screen.findByRole("button", { name: "保存并设置每天重跑" })).toBeTruthy();
    expect(screen.getAllByText("通过").length).toBeGreaterThan(0);
    expect(screen.getByText("工件体积从 8 变成 4，小了 50%")).toBeTruthy();
    expect(screen.getByText("旁边的牙没有变化")).toBeTruthy();
    expect(screen.getByRole("img", { name: "之前" })).toBeTruthy();
    expect(screen.getByRole("img", { name: "之后" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "保存并设置每天重跑" }));
    fireEvent.change(screen.getByLabelText("每天重跑的时间"), { target: { value: "07:30" } });
    await user.click(screen.getByRole("button", { name: "保存安排" }));
    expect(screen.getByRole("status").textContent).toContain("到 07:30 会再跑一遍");
    await user.click(screen.getByRole("button", { name: "回到首页" }));
    expect(screen.getByRole("heading", { name: "已保存的测试" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "新建一个测试" })).toBeTruthy();
    expect(screen.getByText("通过")).toBeTruthy();
    expect(screen.getByText(/下次 (今天|明天) 07:30/)).toBeTruthy();
  });

  it("filters, renames, pauses, and deletes a longer list", async () => {
    sessionStorage.setItem("workbench.author.samples", "1");
    const user = userEvent.setup();
    render(<ExecutorStudio />);
    expect(screen.getByRole("group", { name: "筛选" })).toBeTruthy();
    expect(screen.getAllByText("没通过").length).toBeGreaterThan(0);
    await user.click(screen.getByRole("button", { name: "通过", pressed: false }));
    expect(screen.queryByText("旁边的牙变了")).toBeNull();
    await user.click(screen.getByRole("button", { name: "全部" }));
    await user.click(screen.getByRole("button", { name: /^切削后体积减小通过/ }));
    expect(await screen.findByText("工件体积从 8 变成 4，小了 50%")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "返回首页" }));
    await user.click(screen.getByRole("button", { name: "旁边的牙变了 更多" }));
    await user.click(screen.getByRole("menuitem", { name: "重命名" }));
    fireEvent.change(screen.getByLabelText("新名称"), { target: { value: "牙的检查" } });
    await user.click(screen.getByRole("button", { name: "保存名称" }));
    expect(screen.getByText("牙的检查")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "牙的检查 更多" }));
    await user.click(screen.getByRole("menuitem", { name: "暂停每天重跑" }));
    expect(screen.getByRole("button", { name: /^牙的检查没通过/ }).textContent).toContain("每天重跑已暂停");
    await user.click(screen.getByRole("button", { name: "牙的检查 更多" }));
    await user.click(screen.getByRole("menuitem", { name: "删除" }));
    expect(screen.getByRole("dialog", { name: "确认删除" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "取消" }));
    expect(screen.getByText("牙的检查")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "牙的检查 更多" }));
    await user.click(screen.getByRole("menuitem", { name: "删除" }));
    await user.click(screen.getByRole("button", { name: "删除" }));
    expect(screen.queryByText("牙的检查")).toBeNull();
  });

  it("explains a failed check and offers another try", async () => {
    sessionStorage.setItem("workbench.author.fake", "fail");
    const user = userEvent.setup();
    render(<ExecutorStudio stepDelay={0} />);
    await openSample(user);
    await user.click(screen.getByRole("button", { name: "演示一遍" }));
    expect(await screen.findByRole("button", { name: "重新演示" })).toBeTruthy();
    expect(screen.getAllByText("没通过").length).toBeGreaterThan(0);
    expect(screen.getByText("画面上的文字不对。")).toBeTruthy();
    expect(screen.getByText("期望看到「不会出现」")).toBeTruthy();
    expect(screen.getByText("实际看到「已开始」")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "修改检查" }));
    expect(screen.getByRole("heading", { name: "看它准备做什么" })).toBeTruthy();
  });

  it("stops generating when today's count is used up", async () => {
    localStorage.setItem(capStorageKey, "0");
    const user = userEvent.setup();
    render(<ExecutorStudio complete={async () => ({ text: fixedReplayScript, cost: 0 })} />);
    await openSample(user);
    expect(screen.getByText(authorMessagesOver())).toBeTruthy();
    expect(screen.getByText("今天还能生成 0 次。")).toBeTruthy();
  });
});

function authorMessagesOver(): string {
  return "今天已经问得够多了。先用保存好的检查，或把每天的次数调大。";
}

describe("executor script", () => {
  it("keeps screenshots out of the recorded script and diffs pixels locally", () => {
    const steps = recordFromDom(sampleDom);
    expect(steps.some((item) => item.action === "click" && item.selector === "#start")).toBe(true);
    expect(JSON.stringify(steps)).not.toContain("data:image");
    const result = replay({ ...seedCases()[0], steps }, sampleDom);
    expect(result.passed).toBe(true);
    expect(result.steps.find((item) => item.id === "rec-shot")?.evidence.startsWith("local:")).toBe(true);
    expect(mismatch(new Uint8Array([1, 2]), new Uint8Array([1, 9]), 0)).toBeGreaterThan(0);
    expect(repairPrompt(sampleDom, steps[1])).not.toContain("data:image");
    const authored = authorPrompt("切削工件后体积减小，邻件不变", sampleDom, sceneSnapshotText);
    expect(authored).toContain("schemaVersion 必须是 1");
    expect(readAuthoredScript('{"schema":"workbench.test-executor.authored-script.v1","schemaVersion":2,"id":"A","name":"n","steps":[{"id":"s","type":"web","action":"click"}]}')).toBeNull();
    const saved = caseFromScript(fixedReplayScript);
    expect(replay(saved!, sampleDom).passed).toBe(true);
    const plan = plainPlan(saved!);
    expect(plan.does.length).toBeLessThanOrEqual(4);
    expect(plan.checks.filter((line) => line === "旁边的牙保持原样")).toHaveLength(1);
    const findings = humanFindings(saved!, replay(saved!, sampleDom));
    expect(findings.some((item) => item.sentence.includes("小了 50%"))).toBe(true);
    expect(replay(caseFromScript(failingReplayScript)!, sampleDom).passed).toBe(false);
    expect(connectChoice("Cutter.exe").ok).toBe(true);
    expect(connectChoice("notes.txt").ok).toBe(false);
    expect(describeStep(saved!.steps[1]).will).toContain("点击");
    expect(resultSentence(false)).toContain("没有达到预期");
    writeDailyCap(3);
    expect(readDailyCap()).toBe(3);
    const now = new Date(2026, 10, 13, 3, 26, 0);
    expect(formatWhen(now, now)).toBe(`今天 ${now.toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit", hour12: false })}`);
    const tomorrow = new Date(now);
    tomorrow.setDate(now.getDate() + 1);
    tomorrow.setHours(7, 30, 0, 0);
    expect(formatWhen(tomorrow, now)).toBe(`明天 ${tomorrow.toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit", hour12: false })}`);
    const older = new Date(2026, 10, 12, 3, 26, 0);
    expect(formatWhen(older, now)).toMatch(/^11月12日 /);
    expect(nextDailyInstant("07:30", now).getDate()).toBe(13);
    expect(sampleLibrary(now)).toHaveLength(8);
    expect(sampleLibrary(now).some((item) => !item.passed)).toBe(true);
  });
});
