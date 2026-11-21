import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ApiClient } from "./api";
import { HomePage } from "./launcher";
import { ImportPage, InstalledPage, MarketDetail, MarketPage, UpdatesPage } from "./market";
import { writeAiSettings } from "./executor/model";
import { AiSettingsPage, DiagnosticsPage, OfflinePage, PairingPage, PluginWorkspace, SampleCasesPage, SettingsPage } from "./workspace";

afterEach(() => cleanup());

const emptyDigest = {
  schema: "workbench.upcam.digest.v2.preview", schemaVersion: 2, autoSend: false, delivered: false, stored: false, source: "none",
  meetingDate: "2026-11-12", summary: "还没有今日草稿。", speechSummary: "", items: [], speeches: [], skipped: [], issues: [],
};
const notesManifest = JSON.stringify({
  contract: "workbench.plugin.v2", contractVersion: "2.0.0-preview.1", id: "com.example.synthetic-notes",
  name: "合成备忘", version: "0.1.0", description: "只留在本机的合成清单。", autoSend: false,
  backend: { entryAssembly: "Notes.dll", entryType: "Example.Notes" },
  ui: { kind: "web-package", module: "./ui/Notes.tsx", mount: "embedded" },
  permissions: [{ id: "project.read", reason: "知道当前项目", sensitive: false }],
});
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
    expect(screen.getByText("还没有打开过。从下面选一个，关掉再打开也会记得。")).toBeTruthy();
    expect(screen.getByRole("button", { name: /自动测试/ })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: /UPCAM 站会日报/ }));
  });

  it("opens today's automatic-test summary on the tests that did not pass", async () => {
    sessionStorage.setItem("workbench.author.summary", JSON.stringify({ ran: 8, passed: 7, failed: 1, needsUpdate: 0 }));
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={["/"]}><Routes><Route path="/" element={<HomePage onOpenPalette={vi.fn()} />} /><Route path="/plugin/:id" element={<p>打开了自动测试</p>} /></Routes></MemoryRouter>);
    const link = screen.getByRole("link", { name: /今天跑了 8 个，7 个通过，1 个没通过/ });
    expect(link.getAttribute("href")).toBe("/plugin/runner?filter=todo");
    await user.click(link);
    expect(screen.getByText("打开了自动测试")).toBeTruthy();
    sessionStorage.removeItem("workbench.author.summary");
  });

  it("shows only plugins read from this computer", async () => {
    const user = userEvent.setup();
    const onOpen = vi.fn();
    render(<MemoryRouter><MarketPage onOpen={onOpen} plugins={[
      { id: "test", openId: "test", name: "测试设计", version: "0.1.0", icon: "flask", state: "可以用", loaded: true, href: "/plugin/test" },
      { id: "ding", openId: "ding", name: "钉钉 Insights", version: "0.1.0", icon: "chat", state: "没能载入", loaded: false, href: "/plugin/ding" },
    ]} /></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "浏览" })).toBeTruthy();
    expect(screen.getByText("这里只列出这台电脑上读到的插件。没有网上目录，也不能从这里安装。")).toBeTruthy();
    expect(screen.getByText("可以用")).toBeTruthy();
    expect(screen.getAllByText("本机版本 0.1.0").length).toBe(2);
    expect(screen.getByRole("button", { name: "不能打开" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "安装" })).toBeNull();
    expect(screen.queryByText("会议纪要助手")).toBeNull();
    await user.click(screen.getByRole("button", { name: "打开" }));
    expect(onOpen).toHaveBeenCalledWith("test");
    cleanup();
    render(<MemoryRouter><MarketPage onOpen={vi.fn()} plugins={[]} /></MemoryRouter>);
    expect(screen.getByText("这台电脑上还没有读到插件。")).toBeTruthy();
  });

  it("shows installed detail, updates, and the import checklist", async () => {
    const user = userEvent.setup();
    const onPin = vi.fn();
    const { unmount } = render(<MemoryRouter><InstalledPage onOpen={vi.fn()} pinned={["upcam"]} onPin={onPin} /></MemoryRouter>);
    await user.click(screen.getAllByRole("button", { name: /钉钉 Insights/ })[0]);
    await user.click(screen.getByRole("button", { name: "把 钉钉 Insights 固定到左侧" }));
    expect(onPin).toHaveBeenCalledWith("ding", true);
    unmount();
    render(<MemoryRouter><UpdatesPage plugins={[{ id: "upcam", openId: "upcam", name: "UPCAM 站会日报", version: "0.1.0", icon: "clipboard", state: "可以用", loaded: true, href: "/plugin/upcam" }]} /></MemoryRouter>);
    expect(screen.getByText("没有可下载的更新。下面是这台电脑上的版本，不会从网上下载，也不能在这里卸掉。")).toBeTruthy();
    expect(screen.getByText("本机版本 0.1.0")).toBeTruthy();
    expect(screen.getByText("没有可下载的更新")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "更新全部" })).toBeNull();
    expect(screen.queryByRole("button", { name: "安装" })).toBeNull();
    cleanup();
    render(<MemoryRouter><UpdatesPage plugins={[]} /></MemoryRouter>);
    expect(screen.getByText("这台电脑上还没有读到插件。")).toBeTruthy();
    cleanup();
    const onCheck = vi.fn(async () => ({ importable: false, error: "InvalidManifest", fileName: "sample.plugin.v2.json", plugin: null, checks: [{ id: "manifest", ok: false, text: "这不是插件清单。请选择 plugin.v2.json。" }] }));
    render(<MemoryRouter><ImportPage onCheck={onCheck} onCommit={vi.fn()} /></MemoryRouter>);
    const file = new File(["{}"], "sample.plugin.v2.json", { type: "application/json" });
    await user.upload(screen.getByLabelText("选择文件"), file);
    expect(await screen.findByText("这不是插件清单。请选择 plugin.v2.json。")).toBeTruthy();
    expect((screen.getByRole("button", { name: "确认记在这台电脑上" }) as HTMLButtonElement).disabled).toBe(true);
    cleanup();
    const onCommit = vi.fn(async () => undefined);
    render(<MemoryRouter><ImportPage onCheck={async () => ({ importable: true, error: null, fileName: "notes.plugin.v2.json", plugin: { id: "com.example.synthetic-notes", name: "合成备忘", version: "0.1.0" }, checks: [{ id: "manifest", ok: true, text: "这是一份插件清单。" }, { id: "permissions", ok: true, text: "允许「project.read」：知道当前项目。不会联网。" }] })} onCommit={onCommit} /></MemoryRouter>);
    await user.upload(screen.getByLabelText("选择文件"), new File([notesManifest], "notes.plugin.v2.json", { type: "application/json" }));
    expect(await screen.findByText("这是一份插件清单。")).toBeTruthy();
    expect(screen.getByText("允许「project.read」：知道当前项目。不会联网。")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "确认记在这台电脑上" }));
    expect(await screen.findByText("「合成备忘」已记在这台电脑上，还没有启用。")).toBeTruthy();
    expect(onCommit).toHaveBeenCalledWith("notes.plugin.v2.json", notesManifest);
    cleanup();
    render(<MemoryRouter><ImportPage onCheck={async () => { throw new Error("offline"); }} onCommit={vi.fn()} /></MemoryRouter>);
    await user.upload(screen.getByLabelText("选择文件"), new File(["{}"], "broken.json", { type: "application/json" }));
    expect(await screen.findByText("没能检查这个文件。没有写入。")).toBeTruthy();
    cleanup();
    render(<MemoryRouter><ImportPage onCheck={async () => ({ importable: true, error: null, fileName: "notes.plugin.v2.json", plugin: null, checks: [{ id: "manifest", ok: true, text: "这是一份插件清单。" }] })} onCommit={async () => { throw new Error("refused"); }} /></MemoryRouter>);
    await user.upload(screen.getByLabelText("选择文件"), new File(["{}"], "notes.plugin.v2.json", { type: "application/json" }));
    await user.click(await screen.findByRole("button", { name: "确认记在这台电脑上" }));
    expect(await screen.findByText("没有记下。请看上面的检查。")).toBeTruthy();
  });

  it("asks before enabling a recorded plugin and keeps it disabled when that fails", async () => {
    const user = userEvent.setup();
    const onEnable = vi.fn(async () => undefined);
    const plugin = { id: "com.example.synthetic-notes", name: "合成备忘", hint: "0.1.0", icon: "folder" as const, state: "已安装，尚未启用", loaded: false };
    const { unmount } = render(<MemoryRouter><InstalledPage onOpen={vi.fn()} onEnable={onEnable} plugins={[plugin]} /></MemoryRouter>);
    await user.click(screen.getAllByRole("button", { name: "启用" })[0]);
    const dialog = screen.getByRole("dialog", { name: "启用「合成备忘」" });
    expect(within(dialog).getByText("只会加载这台电脑上已经记下的清单。不会联网，也不会下载。")).toBeTruthy();
    await user.click(within(dialog).getByRole("button", { name: "启用" }));
    expect(onEnable).toHaveBeenCalledWith("com.example.synthetic-notes");
    unmount();
    render(<MemoryRouter><InstalledPage onOpen={vi.fn()} onEnable={async () => { throw new Error("没有启用。记下的清单已经不在。"); }} plugins={[plugin]} /></MemoryRouter>);
    await user.click(screen.getAllByRole("button", { name: "启用" })[0]);
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "启用" }));
    expect(await screen.findByText("没有启用。记下的清单已经不在。")).toBeTruthy();
    expect(screen.getAllByRole("button", { name: "启用" }).length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: "打开" })).toBeNull();
  });

  it("asks before removing a recorded plugin and keeps it when that fails", async () => {
    const user = userEvent.setup();
    const onRemove = vi.fn(async () => undefined);
    const recorded = { id: "com.example.synthetic-notes", name: "合成备忘", hint: "0.1.0", icon: "folder" as const, state: "可以用", loaded: true, recorded: true };
    const shipped = { id: "upcam", name: "UPCAM 站会日报", hint: "整理今日站会", icon: "clipboard" as const, state: "可以用", loaded: true };
    const { unmount } = render(<MemoryRouter><InstalledPage onOpen={vi.fn()} onRemove={onRemove} plugins={[recorded, shipped]} /></MemoryRouter>);
    expect(screen.getAllByRole("button", { name: "卸掉" }).length).toBe(2);
    expect(screen.getByText(/本地导入记下的，可以卸掉记下的清单/)).toBeTruthy();
    await user.click(screen.getAllByRole("button", { name: "卸掉" })[0]);
    const dialog = screen.getByRole("dialog", { name: "卸掉「合成备忘」" });
    expect(within(dialog).getByText(/不会删除你当初选中的原文件/)).toBeTruthy();
    await user.click(within(dialog).getByRole("button", { name: "卸掉" }));
    expect(onRemove).toHaveBeenCalledWith("com.example.synthetic-notes");
    unmount();
    render(<MemoryRouter><InstalledPage onOpen={vi.fn()} onRemove={async () => { throw new Error("没有卸掉。记下的清单还在。"); }} plugins={[recorded]} /></MemoryRouter>);
    await user.click(screen.getAllByRole("button", { name: "卸掉" })[0]);
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "卸掉" }));
    expect(await screen.findByText("没有卸掉。记下的清单还在。")).toBeTruthy();
    expect(screen.getAllByText("合成备忘").length).toBeGreaterThan(0);
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
    render(<DiagnosticsPage minimumWidth={1100} discovered={4} ready={3} />);
    expect(screen.getByText(/仅本机回环/)).toBeTruthy();
    expect(screen.getByText("未开放")).toBeTruthy();
    expect(screen.getByText("没有。不会从网上下载插件。")).toBeTruthy();
    expect(screen.getByText("4 个")).toBeTruthy();
    expect(screen.getByText("3 个")).toBeTruthy();
    expect(screen.getByText(/1,100/)).toBeTruthy();
    expect(screen.queryByText("LAN 发现")).toBeNull();
    cleanup();
    render(<MemoryRouter><PairingPage /></MemoryRouter>);
    expect(screen.getByText("局域网没有开放，不能配对另一台设备，也没有配对过别的电脑。")).toBeTruthy();
    cleanup();
    render(<MemoryRouter><OfflinePage onRetry={vi.fn()} /></MemoryRouter>);
    await user.click(screen.getByRole("button", { name: "重试" }));
    cleanup();
    render(<MemoryRouter initialEntries={["/plugin/test/stopped"]}><PluginWorkspace client={client} crashed /></MemoryRouter>);
    expect(screen.getByRole("heading", { name: /已停止运行/ })).toBeTruthy();
    expect(screen.getByText("renderer timeout after 12s")).toBeTruthy();
    cleanup();
    render(<MemoryRouter initialEntries={["/plugin/runner"]}><Routes><Route path="/plugin/:id" element={<PluginWorkspace client={client} empty="target" />} /></Routes></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "还没连上要测的程序" })).toBeTruthy();
    cleanup();
    render(<MemoryRouter initialEntries={["/plugin/runner"]}><Routes><Route path="/plugin/:id" element={<PluginWorkspace client={client} empty="agent" />} /></Routes></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "另一台电脑没有回应" })).toBeTruthy();
    cleanup();
    render(<MemoryRouter initialEntries={["/plugin/runner"]}><Routes><Route path="/plugin/:id" element={<PluginWorkspace client={client} empty="key" />} /></Routes></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "需要先设置 AI 密钥" })).toBeTruthy();
    cleanup();
    sessionStorage.setItem("workbench.author.fake", "1");
    render(<MemoryRouter initialEntries={["/settings/ai?return=/plugin/runner"]}><Routes><Route path="/settings/ai" element={<AiSettingsPage />} /><Route path="/plugin/:id" element={<p>回到向导</p>} /></Routes></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "AI 设置" })).toBeTruthy();
    expect(screen.getAllByText(/每天重跑不会再用 AI/).length).toBeGreaterThan(0);
    fireEvent.change(screen.getByLabelText("密钥"), { target: { value: "sample-key-0001" } });
    expect((screen.getByLabelText("密钥") as HTMLInputElement).type).toBe("password");
    await user.click(screen.getByRole("button", { name: "测试一下" }));
    expect((await screen.findByRole("status")).textContent).toBe("可以用");
    expect(screen.getByRole("status").textContent).not.toContain("sample-key");
    await user.click(screen.getByRole("button", { name: "保存" }));
    expect(screen.getByText("回到向导")).toBeTruthy();
    expect(sessionStorage.getItem("workbench.wizard.resume")).toBe("1");
    cleanup();
    sessionStorage.removeItem("workbench.author.fake");
    writeAiSettings({ provider: "openai", key: "", base: "", cap: 20 });
    localStorage.clear();
    render(<MemoryRouter><AiSettingsPage /></MemoryRouter>);
    await user.click(screen.getByRole("radio", { name: "兼容接口" }));
    fireEvent.change(screen.getByLabelText("接口地址"), { target: { value: "http://127.0.0.1:9" } });
    await user.click(screen.getByRole("button", { name: "测试一下" }));
    expect((await screen.findByRole("status")).textContent).toContain("先填写密钥");
    fireEvent.change(screen.getByLabelText("密钥"), { target: { value: "sample-key-0001" } });
    await user.click(screen.getByRole("button", { name: "测试一下" }));
    expect((await screen.findByRole("status")).textContent).toBe("密钥不对");
    await user.click(screen.getByRole("button", { name: "保存" }));
    expect(screen.getByRole("status").textContent).toBe("已保存");
    expect(screen.getByRole("status").textContent).not.toContain("sample-key");
    expect(screen.getByRole("button", { name: "更换" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "删除" })).toBeTruthy();
    expect(screen.queryByLabelText("密钥")).toBeNull();
    await user.click(screen.getByRole("button", { name: "删除" }));
    expect(screen.getByLabelText("密钥")).toBeTruthy();
    cleanup();
    sessionStorage.clear();
    render(<MemoryRouter initialEntries={["/plugin/runner"]}><Routes><Route path="/plugin/:id" element={<PluginWorkspace client={client} />} /></Routes></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "还没有测试" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "新建一个测试" }));
    await user.click(screen.getByRole("button", { name: "合成电子样品" }));
    expect(screen.getByRole("status").textContent).toContain("已经打开");
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
    render(<MemoryRouter initialEntries={["/market/minutes"]}><Routes><Route path="/market/:id" element={<MarketDetail plugins={[]} />} /></Routes></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "这台电脑上没有这个插件" })).toBeTruthy();
    expect(screen.getByText("没有去网上查，也不能从这里安装。")).toBeTruthy();
    cleanup();
    render(<MemoryRouter initialEntries={["/market/com.example.synthetic-notes"]}><Routes><Route path="/market/:id" element={<MarketDetail plugins={[{ id: "com.example.synthetic-notes", openId: "com.example.synthetic-notes", name: "合成备忘", version: "0.1.0", icon: "folder", state: "已安装，尚未启用", loaded: false, href: "" }]} />} /></Routes></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "合成备忘" })).toBeTruthy();
    expect(screen.getByText("0.1.0")).toBeTruthy();
    expect(screen.getByText("这台电脑")).toBeTruthy();
    expect((screen.getByRole("button", { name: "尚未启用" }) as HTMLButtonElement).disabled).toBe(true);
    cleanup();
    const onOpen = vi.fn();
    render(<MemoryRouter initialEntries={["/market/com.projectworkbench.upcam-standup.v2"]}><Routes><Route path="/market/:id" element={<MarketDetail onOpen={onOpen} plugins={[{ id: "com.projectworkbench.upcam-standup.v2", openId: "upcam", name: "UPCAM 站会日报", version: "0.1.0", icon: "clipboard", state: "可以用", loaded: true, href: "/plugin/upcam" }]} />} /></Routes></MemoryRouter>);
    await user.click(screen.getByRole("button", { name: "打开" }));
    expect(onOpen).toHaveBeenCalledWith("upcam");
  });
});
