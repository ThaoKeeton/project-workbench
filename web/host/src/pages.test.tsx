import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ApiClient } from "./api";
import type { Snapshot } from "./App";
import { CasesPage, DevicesPage, DiagnosticsPage, HomePage, PluginsPage, ProjectsPage, SettingsPage } from "./pages";

afterEach(() => cleanup());

const snapshot: Snapshot = {
  info: {
    product: "ProjectWorkbench",
    apiVersion: "2.0.0-preview.1",
    preview: true,
    device: { id: "local", label: "本机", kind: "loopback" },
    lan: { enabled: false, availableInPreview: false },
    autoSend: false,
    catalogSchemaVersion: 1,
  },
  projects: {
    schemaVersion: 1,
    revision: "rev-1",
    activeProjectId: "default",
    projects: [
      { id: "default", name: "默认示例", isArchived: false },
      { id: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", name: "示例项目", isArchived: false },
    ],
  },
  plugins: [
    { id: "com.projectworkbench.summary", name: "工作摘要 · 示例", version: "0.1.0", contract: "v1", state: "Disabled", trusted: false, loaded: false, diagnostics: [], permissions: [], uiPath: null },
    { id: "com.projectworkbench.test-cases.v2", name: "测试用例 · 示例", version: "0.1.0", contract: "v2-preview", state: "Ready", trusted: true, loaded: true, diagnostics: [], permissions: [{ id: "local.file.export", sensitive: false }], uiPath: "/plugins/test-cases" },
  ],
  diagnostics: {
    schema: "workbench.diagnostics.v2.preview",
    service: { bind: "loopback", lanEnabled: false, autoSend: false },
    plugins: { discovered: 2, ready: 1, invalid: 0, loaded: 1 },
    projects: { count: 2, activePresent: true },
    issues: [],
    nativeWindow: { minimumWidth: 1100 },
  },
  channel: "已连接",
};

function client(): ApiClient {
  return {
    switchProject: vi.fn(async () => ({ ...snapshot.projects, activeProjectId: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", revision: "rev-2" })),
    info: vi.fn(async () => snapshot.info),
    plugins: vi.fn(async () => snapshot.plugins),
    diagnostics: vi.fn(async () => snapshot.diagnostics),
    cases: vi.fn(async () => ({ schema: "workbench.test-cases.v2.preview", schemaVersion: 2, suiteName: "原生工作台演示", rows: [{ id: "EX-001", action: "启用本地插件", expected: "页面出现在主窗口" }, { id: "EX-002", action: "编辑摘要示例", expected: "草稿保存到本地数据目录" }, { id: "EX-003", action: "停用示例插件", expected: "页面与命令入口撤销" }] })),
    saveCases: vi.fn(async (document) => document),
    exportCases: vi.fn(async () => new Blob(["suite,id\n"])),
  } as unknown as ApiClient;
}

describe("preview pages", () => {
  it("renders the overview, plugins, diagnostics, and devices", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><HomePage snapshot={snapshot} /></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "今天的工作台" })).toBeTruthy();
    for (const name of ["配对设备", "查看更新", "打开诊断", "生成预览", "打开检查", "查看阶段", "管理", "去更新", "查看"]) {
      await user.click(screen.getByRole("button", { name }));
    }
    expect(screen.getByText("本机 · 星河笔记本")).toBeTruthy();
    cleanup();
    render(<MemoryRouter><PluginsPage plugins={snapshot.plugins} /></MemoryRouter>);
    expect(document.body.textContent).toContain("未启用");
    cleanup();
    const withIssue: Snapshot = { ...snapshot, diagnostics: { ...snapshot.diagnostics, service: { ...snapshot.diagnostics.service, bind: "other" }, issues: [{ code: "InvalidManifest" }] } };
    render(<DiagnosticsPage snapshot={withIssue} />);
    expect(document.body.textContent).toContain("InvalidManifest");
    cleanup();
    render(<DevicesPage />);
    expect(screen.getByRole("heading", { name: "设备" })).toBeTruthy();
  });

  it("switches theme and project", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<SettingsPage setting="system" onChange={onChange} />);
    await user.click(screen.getByRole("radio", { name: "深色" }));
    expect(onChange).toHaveBeenCalledWith("dark");
    cleanup();
    const api = client();
    const onSwitched = vi.fn();
    render(<ProjectsPage client={api} snapshot={snapshot} onSwitched={onSwitched} />);
    await user.click(screen.getByRole("button", { name: "切换到此项目" }));
    expect(onSwitched).toHaveBeenCalled();
  });

  it("loads the sample cases", async () => {
    const user = userEvent.setup();
    URL.createObjectURL = () => "blob:preview";
    URL.revokeObjectURL = () => undefined;
    render(<MemoryRouter><CasesPage client={client()} /></MemoryRouter>);
    const step = await screen.findByDisplayValue("启用本地插件");
    await user.clear(step);
    await user.type(step, "查看本地示例");
    await user.click(screen.getByRole("button", { name: "保存示例草稿" }));
    expect(await screen.findByText("已保存到本机，未发送。")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "导出本地 CSV" }));
    expect(await screen.findByText("已导出到本机，未发送。")).toBeTruthy();
  });
});
