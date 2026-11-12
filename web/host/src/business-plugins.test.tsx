import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DingPanel, readChatFile, type InsightState } from "../../../plugins/v2/Workbench.DingTalkInsights.V2/ui/DingPanel";
import { TestingPanel, type Dashboard } from "../../../plugins/v2/Workbench.TestingDesign.V2/ui/TestingPanel";
import { readLocalNotes, UpcamPanel, type Digest } from "../../../plugins/v2/Workbench.UpcamStandup.V2/ui/UpcamPanel";

afterEach(() => cleanup());

const digest: Digest = {
  schema: "workbench.upcam.digest.v2.preview",
  schemaVersion: 2,
  autoSend: false,
  delivered: false,
  stored: true,
  source: "synthetic-sample",
  meetingDate: "2026-11-12",
  summary: "今日 2026-11-12 共 1 项。自动发送已关闭，草稿只留在本机。",
  speechSummary: "界面：登录页今天完成对照。",
  items: [{ id: "SYN-014", title: "登录页对照", owner: "界面", status: "进行中" }],
  speeches: [{ owner: "界面", text: "登录页今天完成对照。" }],
  skipped: [{ name: "yesterday.txt", reason: "NotToday" }],
  issues: ["有一条事项编号无法识别"],
};

const insights: InsightState = {
  schema: "workbench.dingtalk.insights.v2.preview",
  schemaVersion: 2,
  autoSend: false,
  sendEnabled: false,
  webhookConfigured: false,
  delivered: false,
  stored: true,
  source: "synthetic-sample",
  imported: true,
  analyzed: true,
  conversation: "合成项目群",
  messageCount: 6,
  insights: [{ id: "topic-login-review", kind: "topic", title: "登录页对照出现 3 次", detail: "只按原文计数。", evidence: [{ messageId: "M-001", excerpt: "登录页对照今天收尾。" }] }],
};

const board: Dashboard = {
  schema: "workbench.testing.dashboard.v2.preview",
  schemaVersion: 2,
  autoSend: false,
  label: "合成检查，不替代人工验收",
  notice: "这些用例和运行记录来自合成样本。",
  humanAcceptance: false,
  summary: { passed: 1, failed: 0, total: 1 },
  cases: [{ id: "SYN-TC-001", title: "打开合成站会记录", suite: "界面" }],
  runs: [{ id: "SYN-RUN-001", caseId: "SYN-TC-001", status: "通过", kind: "合成检查", humanAcceptance: "未验收" }],
};

describe("business plugins", () => {
  it("imports standup notes and renders today's draft", async () => {
    const user = userEvent.setup();
    const load = vi.fn(async () => ({ ...digest, stored: false, items: [], speeches: [], skipped: [], issues: [] }));
    const importNotes = vi.fn(async () => digest);
    const loadSample = vi.fn(async () => digest);
    render(<UpcamPanel load={load} importNotes={importNotes} loadSample={loadSample} />);
    expect(await screen.findByRole("heading", { name: "站会日报" })).toBeTruthy();
    expect(screen.getAllByText(/自动发送已关闭/).length).toBeGreaterThan(0);
    await user.click(screen.getByRole("button", { name: "载入合成样本" }));
    expect(await screen.findByText("SYN-014")).toBeTruthy();
    expect(screen.getAllByText("界面").length).toBeGreaterThan(0);
    expect(screen.getByText("进行中")).toBeTruthy();
    expect(screen.getByText(/不是今日记录/)).toBeTruthy();
    expect(await screen.findByText("已载入合成样本，未发送。")).toBeTruthy();
    const file = new File(["日期: 2026-11-12\n"], "notes.txt", { type: "text/plain" });
    await user.upload(screen.getByLabelText("选择站会文件"), file);
    expect(importNotes).toHaveBeenCalledWith([{ name: "notes.txt", text: "日期: 2026-11-12\n" }]);
    await expect(readLocalNotes([new File(["{}"], "skip.png")])).rejects.toThrow("NoText");
    await expect(readLocalNotes([new File(["x".repeat(65537)], "big.txt")])).rejects.toThrow("FileTooLarge");
    cleanup();
    render(<UpcamPanel load={async () => { throw new Error("nope"); }} importNotes={importNotes} loadSample={loadSample} />);
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "无法读取站会草稿。");
    cleanup();
    render(<UpcamPanel load={async () => digest} importNotes={async () => { throw new Error("nope"); }} loadSample={loadSample} />);
    await user.upload(screen.getByLabelText("选择站会文件"), new File(["x".repeat(65537)], "big.txt"));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "文件太大。");
    await user.upload(screen.getByLabelText("选择站会文件"), new File(["{}"], "notes.txt"));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "没有写入草稿。");
  });

  it("analyzes an imported chat and keeps send disabled", async () => {
    const user = userEvent.setup();
    const pending = { ...insights, analyzed: false, insights: [] };
    const load = vi.fn(async () => ({ ...pending, imported: false, messageCount: 0, conversation: "" }));
    const importChat = vi.fn(async () => pending);
    const loadSample = vi.fn(async () => pending);
    const analyze = vi.fn(async () => insights);
    render(<DingPanel load={load} importChat={importChat} loadSample={loadSample} analyze={analyze} />);
    expect(await screen.findByRole("heading", { name: "钉钉 Insights" })).toBeTruthy();
    expect((screen.getByRole("button", { name: "发送已关闭" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole("button", { name: "按需分析" }) as HTMLButtonElement).disabled).toBe(true);
    await user.click(screen.getByRole("button", { name: "载入合成样本" }));
    expect(await screen.findByText(/尚未分析/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "按需分析" }));
    expect(await screen.findByText("登录页对照出现 3 次")).toBeTruthy();
    expect(screen.getByText("M-001")).toBeTruthy();
    expect((screen.getByRole("button", { name: "发送已关闭" }) as HTMLButtonElement).disabled).toBe(true);
    const file = new File(["会话: 合成项目群\n"], "chat.txt", { type: "text/plain" });
    await user.upload(screen.getByLabelText("选择聊天导出"), file);
    expect(importChat).toHaveBeenCalledWith("会话: 合成项目群\n");
    await expect(readChatFile(new File(["nope"], "chat.bin"))).rejects.toThrow("NoText");
    await user.upload(screen.getByLabelText("选择聊天导出"), new File(["x".repeat(65537)], "big.txt"));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "文件太大。");
    cleanup();
    render(<DingPanel load={async () => { throw new Error("nope"); }} importChat={importChat} loadSample={loadSample} analyze={analyze} />);
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "无法读取洞察。");
  });

  it("lists synthetic cases and runs without claiming human acceptance", async () => {
    const user = userEvent.setup();
    const load = vi.fn(async () => board);
    render(<TestingPanel load={load} />);
    expect(await screen.findByRole("heading", { name: "测试设计" })).toBeTruthy();
    expect(screen.getByRole("note").textContent).toContain("不替代人工验收");
    expect(screen.getAllByText("SYN-TC-001").length).toBeGreaterThan(0);
    expect(screen.getAllByText("未验收").length).toBeGreaterThan(0);
    expect(screen.getByText(/合成检查 1 \/ 1 通过/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "刷新合成看板" }));
    expect(await screen.findByText("已读取本机合成看板。")).toBeTruthy();
    cleanup();
    render(<TestingPanel load={vi.fn(async () => { throw new Error("nope"); })} />);
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "无法读取合成看板。");
    cleanup();
    render(<TestingPanel load={vi.fn(async () => ({ ...board, humanAcceptance: true }))} />);
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "无法读取合成看板。");
  });
});
