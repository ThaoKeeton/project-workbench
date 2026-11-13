import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";
import { aiBaseStorageKey, aiKeyStorageKey, appFromFileName, authorPrompt, capStorageKey, caseFromScript, compatiblePresets, connectApp, connectChoice, describeStep, failingReplayScript, fixedReplayScript, formatDailySummary, formatWhen, hintDismissedKey, humanFindings, keepRecentImages, mismatch, missedBecauseOff, nextDailyInstant, outcomeOf, plainPlan, probeAi, readAiSettings, readAuthoredScript, readDailyCap, recordFromDom, repairPrompt, replay, resultSentence, sampleAppName, sampleApps, sampleDom, sampleLibrary, sceneObjectCount, sceneSnapshotText, seedCases, shouldToast, writeAiSettings, writeDailyCap } from "./model";
import { ExecutorStudio } from "./studio";

afterEach(() => {
  cleanup();
  sessionStorage.clear();
  localStorage.removeItem(capStorageKey);
  localStorage.removeItem(hintDismissedKey);
  localStorage.removeItem(aiKeyStorageKey);
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
    expect(screen.getByRole("note", { name: "第一次使用" }).textContent).toContain("先选一个程序。");
    expect(screen.getByText("用一句话说明要检查什么。")).toBeTruthy();
    expect(screen.getByText("看它做一遍，对了再保存。")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "知道了" }));
    expect(screen.queryByRole("note", { name: "第一次使用" })).toBeNull();
    const file = new File(["x"], "notes.txt", { type: "text/plain" });
    await user.click(screen.getByRole("button", { name: "选择程序" }));
    fireEvent.change(screen.getByLabelText("浏览程序"), { target: { files: [file] } });
    expect(screen.getByRole("status").textContent).toContain("还不能自动测试");
    await user.click(screen.getByRole("button", { name: "重新选择" }));
    await user.click(screen.getByRole("button", { name: sampleAppName }));
    expect(screen.getByRole("status").textContent).toContain("已连接：合成电子样品");
    expect(screen.getByText("认出了 3D 画面，里面有 2 个物体")).toBeTruthy();
    expect(screen.getByRole("img", { name: "程序画面" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "下一步" }));
    expect((screen.getByRole("button", { name: "下一步" }) as HTMLButtonElement).disabled).toBe(true);
    await user.type(screen.getByLabelText("要测什么"), "切削工件后，工件变小，旁边的牙不变");
    await user.click(screen.getByRole("button", { name: "下一步" }));
    expect(screen.getByText(/需要先设置 AI 密钥/)).toBeTruthy();
    expect(screen.getByRole("link", { name: "去设置" }).getAttribute("href")).toBe("/settings/ai?return=/plugin/runner");
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
    await user.click(screen.getByRole("button", { name: "现在跑一次" }));
    expect(await screen.findByText("已重新打开「合成电子样品」，跑完后已关掉。")).toBeTruthy();
  });

  it("explains an open app, a non-electron app, and a slow connection", async () => {
    const user = userEvent.setup();
    render(<ExecutorStudio />);
    await user.click(screen.getByRole("button", { name: "知道了" }));
    await user.click(screen.getByRole("button", { name: "选择程序" }));
    await user.click(screen.getByRole("button", { name: "已经开着的样品" }));
    expect(screen.getByRole("status").textContent).toBe("这个程序已经开着，需要由工作台重新打开它");
    await user.click(screen.getByRole("button", { name: "关掉并重新打开" }));
    expect(screen.getByRole("dialog", { name: "确认重新打开" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "取消" }));
    await user.click(screen.getByRole("button", { name: "关掉并重新打开" }));
    await user.click(screen.getAllByRole("button", { name: "关掉并重新打开" })[1]);
    expect(screen.getByRole("status").textContent).toContain("已连接：已经开着的样品");
    await user.click(screen.getByRole("button", { name: "记事本" }));
    expect(screen.getByRole("status").textContent).toContain("还不能自动测试");
    fireEvent.change(screen.getByLabelText("浏览程序"), { target: { files: [new File(["x"], "连不上的样品.exe")] } });
    expect(screen.getByRole("status").textContent).toContain("连上用的时间太长了");
    await user.click(screen.getByRole("button", { name: "再试一次" }));
    expect(screen.getByRole("status").textContent).toContain("已连接：连不上的样品");
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
  it("keeps screenshots out of the recorded script and diffs pixels locally", async () => {
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
    expect(connectChoice("记事本.exe").ok).toBe(false);
    expect(connectApp(sampleApps().find((item) => item.id === "busy") ?? null).state).toBe("busy");
    expect(connectApp(appFromFileName("连不上的样品.exe"), { attempt: 2 }).state).toBe("connected");
    expect(sceneObjectCount(sceneSnapshotText)).toBe(2);
    expect(await probeAi({ key: "short", fake: "pass" })).toMatchObject({ ok: false });
    expect(await probeAi({ key: "sample-key-0001", fake: "pass" })).toMatchObject({ ok: true, message: "可以用" });
    expect(await probeAi({ key: "sample-key-0001", fake: "fail" })).toMatchObject({ ok: false, message: "密钥不对" });
    expect(await probeAi({ key: "sample-key-0001", fake: "off" }, async () => false)).toMatchObject({ ok: false, message: "密钥不对" });
    expect(await probeAi({ key: "sample-key-0001", fake: "off" }, async () => { throw new Error("down"); })).toMatchObject({ ok: false, message: "连不上" });
    expect((await probeAi({ key: "sample-key-0001", fake: "off" }, async () => true)).message).toBe("可以用");
    writeAiSettings({ provider: "compatible", key: "<script>alert(1)</script>", base: "javascript:alert(1)", cap: 4 });
    expect(localStorage.getItem(aiKeyStorageKey)).toBeNull();
    expect(localStorage.getItem(aiBaseStorageKey)).toBeNull();
    expect(readAiSettings().key).toBe("");
    writeAiSettings({ provider: "openai", key: "sample-key-0001", base: "http://127.0.0.1:9", cap: 4 });
    expect(localStorage.getItem(aiKeyStorageKey)).toBeNull();
    expect(JSON.stringify(localStorage)).not.toContain("sample-key");
    expect(readAiSettings().key).toBe("sample-key-0001");
    expect(readAiSettings().base).toBe("http://127.0.0.1:9");
    for (const preset of compatiblePresets) {
      writeAiSettings({ provider: "compatible", key: "", base: preset.base, model: preset.model, preset: preset.id, cap: 20 });
      expect(readAiSettings().base).toBe(preset.base);
      expect(readAiSettings().model).toBe(preset.model);
    }
    expect(compatiblePresets.map((item) => item.base)).toEqual([
      "https://api.deepseek.com",
      "https://dashscope.aliyuncs.com/compatible-mode/v1",
      "https://api.moonshot.cn/v1",
      "https://open.bigmodel.cn/api/paas/v4",
      "",
    ]);
    expect(compatiblePresets.map((item) => item.model)).toEqual([
      "deepseek-flash",
      "qwen3.7-plus",
      "kimi-k3.1-pro",
      "glm-5.3",
      "",
    ]);
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
    expect(nextDailyInstant("07:30", now, [1]).getDay()).not.toBe(5);
    const noon = new Date(2026, 10, 13, 12, 0, 0);
    expect(missedBecauseOff(new Date(2026, 10, 13, 3, 26, 0), "07:30", [1, 2, 3, 4, 5, 6, 7], noon)).toBe(true);
    expect(missedBecauseOff(new Date(2026, 10, 13, 8, 0, 0), "07:30", [1, 2, 3, 4, 5, 6, 7], noon)).toBe(false);
    expect(formatDailySummary({ ran: 8, passed: 7, failed: 1, needsUpdate: 0 })).toBe("今天跑了 8 个，7 个通过，1 个没通过");
    expect(outcomeOf([{ ok: false, kind: "missing" }])).toBe("needs-update");
    expect(outcomeOf([{ ok: false }])).toBe("fail");
    expect(keepRecentImages(Array.from({ length: 31 }, () => ({ image: true }))).filter((item) => item.image)).toHaveLength(30);
    expect(shouldToast("pass")).toBe(false);
    expect(shouldToast("needs-update")).toBe(true);
    expect(sampleLibrary(now)).toHaveLength(8);
    expect(sampleLibrary(now).some((item) => !item.passed)).toBe(true);
  });
});
