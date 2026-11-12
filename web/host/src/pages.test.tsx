import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ApiClient } from "./api";
import { HomePage } from "./launcher";
import { ImportPage, InstalledPage, MarketDetail, MarketPage, UpdatesPage } from "./market";
import { DiagnosticsPage, OfflinePage, PairingPage, PluginWorkspace, SampleCasesPage, SettingsPage } from "./workspace";

afterEach(() => cleanup());

const emptyDigest = {
  schema: "workbench.upcam.digest.v2.preview", schemaVersion: 2, autoSend: false, delivered: false, stored: false, source: "none",
  meetingDate: "2026-11-12", summary: "还没有今日草稿。", speechSummary: "", items: [], speeches: [], skipped: [], issues: [],
};
const emptyInsights = {
  schema: "workbench.dingtalk.insights.v2.preview", schemaVersion: 2, autoSend: false, sendEnabled: false, webhookConfigured: false,
  delivered: false, stored: false, source: "none", imported: false, analyzed: false, conversation: "", messageCount: 0, insights: [],
};
const dashboard = {
  schema: "workbench.testing.dashboard.v2.preview", schemaVersion: 2, autoSend: false, humanAcceptance: false,
  label: "合成检查，不替代人工验收", notice: "这些记录不代表已经人工验收。",
  summary: { passed: 1, failed: 0, total: 1 },
  cases: [{ id: "SYN-TC-001", title: "打开合成站会记录", suite: "界面" }],
  runs: [{ id: "SYN-RUN-001", caseId: "SYN-TC-001", status: "通过", kind: "合成检查", humanAcceptance: "未验收" }],
};
const client = {
  cases: vi.fn(async () => ({ schema: "workbench.test-cases.v2.preview", schemaVersion: 2, suiteName: "原生工作台演示", rows: [{ id: "EX-001", action: "启用本地插件", expected: "页面出现在主窗口" }, { id: "EX-002", action: "编辑摘要示例", expected: "草稿保存到本地数据目录" }, { id: "EX-003", action: "停用示例插件", expected: "页面与命令入口撤销" }] })),
  saveCases: vi.fn(async (document: unknown) => document),
  exportCases: vi.fn(async () => new Blob(["suite,id\n"])),
  upcamDraft: vi.fn(async () => emptyDigest),
  upcamImport: vi.fn(async () => emptyDigest),
  upcamSample: vi.fn(async () => emptyDigest),
  dingState: vi.fn(async () => emptyInsights),
  dingImport: vi.fn(async () => emptyInsights),
  dingSample: vi.fn(async () => emptyInsights),
  dingAnalyze: vi.fn(async () => emptyInsights),
  testingDashboard: vi.fn(async () => dashboard),
} as unknown as ApiClient;

describe("launcher pages", () => {
  it("opens a recent plugin from the launcher", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><HomePage onOpenPalette={vi.fn()} /></MemoryRouter>);
    expect(screen.getByRole("button", { name: /搜索插件、项目、设备或命令/ })).toBeTruthy();
    expect(screen.getByText("合成检查 6 / 6 通过，不替代人工验收")).toBeTruthy();
    expect(screen.getByRole("button", { name: /自动化测试执行器/ })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: /UPCAM 站会日报/ }));
  });

  it("browses the market, confirms an install, and opens the detail", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><MarketPage onOpen={vi.fn()} /></MemoryRouter>);
    await user.click(screen.getByRole("tab", { name: "测试" }));
    expect(screen.getByText("用例仓库")).toBeTruthy();
    await user.click(screen.getAllByRole("button", { name: "安装" })[0]);
    expect(screen.getByRole("dialog")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "取消" }));
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("shows installed detail, updates, and the import checklist", async () => {
    const user = userEvent.setup();
    const { unmount } = render(<MemoryRouter><InstalledPage onOpen={vi.fn()} /></MemoryRouter>);
    await user.click(screen.getAllByRole("button", { name: /钉钉 Insights/ })[0]);
    await user.click(screen.getByRole("button", { name: "卸载" }));
    unmount();
    render(<MemoryRouter><UpdatesPage /></MemoryRouter>);
    await user.click(screen.getByRole("button", { name: /钉钉 Insights/ }));
    expect(screen.getByText("热点计算忽略已撤回的消息")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "更新全部" }));
    expect(screen.getAllByRole("button", { name: "已更新" }).length).toBeGreaterThan(0);
    cleanup();
    render(<MemoryRouter><ImportPage onOpen={vi.fn()} /></MemoryRouter>);
    expect(screen.getByText("未请求网络访问")).toBeTruthy();
    const file = new File(["{}"], "sample.pbpack", { type: "application/octet-stream" });
    await user.upload(screen.getByLabelText("选择文件"), file);
    expect(screen.getByText("sample.pbpack")).toBeTruthy();
  });

  it("covers settings, diagnostics, pairing, offline, crash and empty states", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const onRestore = vi.fn();
    const onMotion = vi.fn();
    render(<SettingsPage setting="system" onChange={onChange} restore onRestore={onRestore} reduceMotion={false} onReduceMotion={onMotion} />);
    await user.click(screen.getByRole("radio", { name: /深色/ }));
    expect(onChange).toHaveBeenCalledWith("dark");
    await user.click(screen.getByRole("button", { name: "开启" }));
    expect(onRestore).toHaveBeenCalledWith(false);
    await user.click(screen.getByRole("button", { name: "关闭" }));
    expect(onMotion).toHaveBeenCalledWith(true);
    cleanup();
    render(<DiagnosticsPage minimumWidth={1100} />);
    expect(screen.getByText(/仅本机回环/)).toBeTruthy();
    expect(screen.getByText(/1,100/)).toBeTruthy();
    cleanup();
    render(<MemoryRouter><PairingPage /></MemoryRouter>);
    expect(screen.getByText("7K4M — XQ2P")).toBeTruthy();
    cleanup();
    render(<MemoryRouter><OfflinePage onRetry={vi.fn()} /></MemoryRouter>);
    await user.click(screen.getByRole("button", { name: "重试" }));
    cleanup();
    render(<MemoryRouter initialEntries={["/plugin/test/stopped"]}><PluginWorkspace client={client} crashed /></MemoryRouter>);
    expect(screen.getByRole("heading", { name: /已停止运行/ })).toBeTruthy();
    expect(screen.getByText("renderer timeout after 12s")).toBeTruthy();
    cleanup();
    render(<MemoryRouter initialEntries={["/plugin/runner"]}><Routes><Route path="/plugin/:id" element={<PluginWorkspace client={client} empty="target" />} /></Routes></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "找不到目标程序" })).toBeTruthy();
    cleanup();
    render(<MemoryRouter initialEntries={["/plugin/runner"]}><Routes><Route path="/plugin/:id" element={<PluginWorkspace client={client} empty="agent" />} /></Routes></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "Agent 未就绪" })).toBeTruthy();
    cleanup();
    render(<MemoryRouter initialEntries={["/plugin/runner"]}><Routes><Route path="/plugin/:id" element={<PluginWorkspace client={client} empty="key" />} /></Routes></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "尚未配置模型密钥" })).toBeTruthy();
    cleanup();
    render(<MemoryRouter initialEntries={["/plugin/runner"]}><Routes><Route path="/plugin/:id" element={<PluginWorkspace client={client} />} /></Routes></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "自动化测试执行器" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "回放" }));
    expect(await screen.findByText("全部通过")).toBeTruthy();
    cleanup();
    URL.createObjectURL = () => "blob:preview";
    URL.revokeObjectURL = () => undefined;
    render(<MemoryRouter><SampleCasesPage client={client} /></MemoryRouter>);
    const step = await screen.findByDisplayValue("启用本地插件");
    const expected = screen.getByLabelText("EX-001 预期");
    await user.clear(step);
    await user.type(step, "查看本地示例");
    await user.clear(expected);
    await user.type(expected, "仍留在本机");
    await user.click(screen.getByRole("button", { name: "保存示例草稿" }));
    expect(await screen.findByText("已保存到本机，未发送。")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "导出本地 CSV" }));
    expect(await screen.findByText("已导出到本机，未发送。")).toBeTruthy();
    cleanup();
    const failing = { ...client, cases: vi.fn(async () => { throw new Error("nope"); }) } as unknown as ApiClient;
    render(<MemoryRouter><SampleCasesPage client={failing} /></MemoryRouter>);
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "无法读取示例用例。");
    cleanup();
    const saving = { ...client, saveCases: vi.fn(async () => { throw new Error("nope"); }) } as unknown as ApiClient;
    render(<MemoryRouter><SampleCasesPage client={saving} /></MemoryRouter>);
    await user.click(await screen.findByRole("button", { name: "保存示例草稿" }));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "没有保存。");
    cleanup();
    const exporting = { ...client, exportCases: vi.fn(async () => { throw new Error("nope"); }) } as unknown as ApiClient;
    render(<MemoryRouter><SampleCasesPage client={exporting} /></MemoryRouter>);
    await user.click(await screen.findByRole("button", { name: "导出本地 CSV" }));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "没有导出。");
    cleanup();
    render(<MemoryRouter initialEntries={["/plugin/ding"]}><Routes><Route path="/plugin/:id" element={<PluginWorkspace client={client} />} /></Routes></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "钉钉 Insights" })).toBeTruthy();
    cleanup();
    render(<MemoryRouter initialEntries={["/market/minutes"]}><Routes><Route path="/market/:id" element={<MarketDetail />} /></Routes></MemoryRouter>);
    expect(screen.getByText("版本历史")).toBeTruthy();
  });
});
