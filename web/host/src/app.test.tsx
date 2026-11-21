import { cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { CommandPalette } from "./launcher";

const notesManifest = JSON.stringify({
  contract: "workbench.plugin.v2", contractVersion: "2.0.0-preview.1", id: "com.example.synthetic-notes",
  name: "合成备忘", version: "0.1.0", description: "只留在本机的合成清单。", autoSend: false,
  backend: { entryAssembly: "Notes.dll", entryType: "Example.Notes" },
  ui: { kind: "web-package", module: "./ui/Notes.tsx", mount: "embedded" },
  permissions: [{ id: "project.read", reason: "知道当前项目", sensitive: false }],
});

const projects = {
  schemaVersion: 1,
  revision: "rev-1",
  activeProjectId: "default",
  projects: [
    { id: "default", name: "星河协作", isArchived: false },
    { id: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", name: "灯塔实验", isArchived: false },
  ],
};

function json(body: unknown, status = 200): Response {
  return new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

beforeEach(() => {
  vi.stubGlobal("matchMedia", () => ({
    matches: false,
    media: "",
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
});

afterEach(() => {
  cleanup();
  localStorage.clear();
  sessionStorage.clear();
  vi.unstubAllGlobals();
});

describe("app shell", () => {
  it("opens the palette, device menu, and a plugin", async () => {
    sessionStorage.setItem("workbench.session", "preview-session");
    vi.stubGlobal("WebSocket", class {
      addEventListener(type: string, handler: (event?: { data?: string }) => void) {
        if (type === "open") handler();
        if (type === "message") handler({ data: JSON.stringify({ type: "project-changed" }) });
      }
      close() { return undefined; }
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith("/api/v2/info")) return json({ product: "ProjectWorkbench", apiVersion: "2", preview: true, device: { id: "local", label: "本机", kind: "loopback" }, lan: { enabled: false, availableInPreview: false }, autoSend: false, catalogSchemaVersion: 1 });
      if (url.endsWith("/api/v2/projects") && init?.method === "POST") return json({ ...projects, activeProjectId: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", revision: "rev-2" });
      if (url.endsWith("/api/v2/projects")) return json(projects);
      if (url.endsWith("/api/v2/plugins")) return json({ plugins: [] });
      if (url.endsWith("/api/v2/diagnostics")) return json({ schema: "workbench.diagnostics.v2.preview", service: { bind: "loopback", lanEnabled: false, autoSend: false }, plugins: { discovered: 0, ready: 0, invalid: 0, loaded: 0 }, projects: { count: 2, activePresent: true }, issues: [], nativeWindow: { minimumWidth: 1100 } });
      if (url.endsWith("/api/v2/ui/theme") && init?.method === "PUT") return json({ schema: "workbench.ui-theme.v2.preview", schemaVersion: 2, setting: "dark" });
      if (url.endsWith("/api/v2/ui/theme")) return json({ schema: "workbench.ui-theme.v2.preview", schemaVersion: 2, setting: "system" });
      if (url.endsWith("/api/v2/session")) return json(null, 204);
      if (url.includes("/cases")) return json({ schema: "workbench.test-cases.v2.preview", schemaVersion: 2, suiteName: "原生工作台演示", rows: [] });
      return json({ error: "NotFound" }, 404);
    }));
    const user = userEvent.setup();
    render(<MemoryRouter><App /></MemoryRouter>);
    expect(await screen.findByRole("button", { name: /搜索插件、项目、设备或命令/ })).toBeTruthy();
    expect(screen.getByText("Project Workbench")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: /星河笔记本/ }));
    expect(screen.getByText("书房工作站")).toBeTruthy();
    await user.click(screen.getByRole("menuitem", { name: /书房工作站/ }));
    await user.keyboard("{Control>}k{/Control}");
    const search = await screen.findByRole("textbox", { name: "搜索插件、项目、设备或命令" });
    await user.type(search, "这台电脑");
    await user.keyboard("{Enter}");
    expect(await screen.findByRole("heading", { name: "浏览" })).toBeTruthy();
    expect(screen.getByText("这台电脑上还没有读到插件。")).toBeTruthy();
    await user.click(screen.getByRole("link", { name: "首页" }));
    await user.click(screen.getByRole("button", { name: "UPCAM 站会日报" }));
    expect(await screen.findByRole("dialog", { name: "远程操作确认" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "确认并继续" }));
    expect(await screen.findByRole("heading", { name: "站会日报" })).toBeTruthy();
  });

  it("keeps a confirmed local manifest installed and tells the truth about this computer", async () => {
    sessionStorage.setItem("workbench.session", "preview-session");
    vi.stubGlobal("WebSocket", class {
      addEventListener(type: string, handler: () => void) { if (type === "open") handler(); }
      close() { return undefined; }
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo, init?: RequestInit) => {
      const url = String(input);
      const body = init?.body ? JSON.parse(String(init.body)) as { text?: string; fileName?: string } : null;
      if (url.endsWith("/api/v2/info")) return json({ product: "ProjectWorkbench", apiVersion: "2", preview: true, device: { id: "local", label: "本机", kind: "loopback" }, lan: { enabled: false, availableInPreview: false }, autoSend: false, catalogSchemaVersion: 1 });
      if (url.endsWith("/api/v2/projects")) return json(projects);
      if (url.endsWith("/api/v2/plugins/import/check") || url.endsWith("/api/v2/plugins/import")) {
        if (body?.text === "{}") return json({ importable: false, error: "InvalidManifest", fileName: body.fileName, plugin: null, checks: [{ id: "manifest", ok: false, text: "这不是插件清单。请选择 plugin.v2.json。" }] });
        return json({ importable: true, error: null, fileName: body?.fileName, plugin: { id: "com.example.synthetic-notes", name: "合成备忘", version: "0.1.0" }, checks: [{ id: "manifest", ok: true, text: "这是一份插件清单。" }, { id: "permissions", ok: true, text: "允许「project.read」：知道当前项目。不会联网。" }] });
      }
      if (url.endsWith("/api/v2/plugins")) return json({ plugins: [
        { id: "com.example.synthetic-notes", name: "合成备忘", version: "0.1.0", contract: "v2-preview", state: "Installed", trusted: false, loaded: false, diagnostics: [], permissions: [], uiPath: null },
        { id: "com.projectworkbench.summary", name: "工作摘要 · 示例", version: "0.1.0", contract: "v1", state: "Disabled", trusted: false, loaded: false, diagnostics: [], permissions: [], uiPath: null },
      ] });
      if (url.endsWith("/api/v2/diagnostics")) return json({ schema: "workbench.diagnostics.v2.preview", service: { bind: "loopback", lanEnabled: false, autoSend: false }, plugins: { discovered: 4, ready: 3, invalid: 0, loaded: 3 }, projects: { count: 2, activePresent: true }, issues: [], nativeWindow: { minimumWidth: 1100 } });
      if (url.endsWith("/api/v2/ui/theme")) return json({ schema: "workbench.ui-theme.v2.preview", schemaVersion: 2, setting: "system" });
      if (url.endsWith("/api/v2/session")) return json(null, 204);
      if (url.endsWith("/api/v2/ui/desk")) return json({ schema: "workbench.ui-desk.v2.preview", schemaVersion: 2, pinned: ["upcam"], recent: [], recovered: false });
      return json({ error: "NotFound" }, 404);
    }));
    const user = userEvent.setup();
    render(<MemoryRouter><App /></MemoryRouter>);
    expect(await screen.findByText("已安装，尚未启用")).toBeTruthy();
    expect(screen.getByText(/另外发现 工作摘要/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: /合成备忘/ }));
    expect(await screen.findByText("「合成备忘」已记在这台电脑上，还没有启用，所以没有打开。")).toBeTruthy();
    await user.click(screen.getByRole("link", { name: "诊断" }));
    expect(await screen.findByText("仅本机回环 · 已连接")).toBeTruthy();
    expect(screen.getByText("4 个")).toBeTruthy();
    expect(screen.getByText("没有。不会从网上下载插件。")).toBeTruthy();
    await user.click(screen.getByRole("link", { name: "设备" }));
    expect(await screen.findByText("局域网没有开放，不能配对另一台设备，也没有配对过别的电脑。")).toBeTruthy();
    await user.click(screen.getByRole("link", { name: "市场" }));
    expect(await screen.findByRole("heading", { name: "浏览" })).toBeTruthy();
    expect(screen.getByText("这里只列出这台电脑上读到的插件。没有网上目录，也不能从这里安装。")).toBeTruthy();
    expect(screen.queryByText("会议纪要助手")).toBeNull();
    expect(screen.getByText("已发现，没有可以打开的页面")).toBeTruthy();
    await user.click(screen.getByRole("link", { name: /合成备忘/ }));
    expect(await screen.findByRole("heading", { name: "合成备忘" })).toBeTruthy();
    expect(screen.getByText("这台电脑")).toBeTruthy();
    expect(screen.getByText("这是这台电脑上读到的记录。没有网上的介绍，也不能从这里安装或卸掉。")).toBeTruthy();
    await user.click(screen.getByRole("link", { name: "更新" }));
    expect(await screen.findByText("没有可下载的更新。下面是这台电脑上的版本，不会从网上下载，也不能在这里卸掉。")).toBeTruthy();
    expect(screen.getAllByText("本机版本 0.1.0").length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: "安装" })).toBeNull();
    await user.click(screen.getByRole("link", { name: "本地导入" }));
    await user.upload(screen.getByLabelText("选择文件"), new File([notesManifest], "notes.plugin.v2.json", { type: "application/json" }));
    expect(await screen.findByText("这是一份插件清单。")).toBeTruthy();
    expect(screen.getByText("允许「project.read」：知道当前项目。不会联网。")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "确认记在这台电脑上" }));
    expect(await screen.findByText("「合成备忘」已记在这台电脑上，还没有启用。")).toBeTruthy();
    await user.upload(screen.getByLabelText("选择文件"), new File(["{}"], "nope.json", { type: "application/json" }));
    expect(await screen.findByText("这不是插件清单。请选择 plugin.v2.json。")).toBeTruthy();
    expect((screen.getByRole("button", { name: "确认记在这台电脑上" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("enables a recorded plugin and then opens it", async () => {
    sessionStorage.setItem("workbench.session", "preview-session");
    let enabled = false;
    vi.stubGlobal("WebSocket", class {
      addEventListener(type: string, handler: () => void) { if (type === "open") handler(); }
      close() { return undefined; }
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith("/api/v2/info")) return json({ product: "ProjectWorkbench", apiVersion: "2", preview: true, device: { id: "local", label: "本机", kind: "loopback" }, lan: { enabled: false, availableInPreview: false }, autoSend: false, catalogSchemaVersion: 1 });
      if (url.endsWith("/api/v2/projects")) return json(projects);
      if (url.endsWith("/enable")) { enabled = true; return json({ id: "com.example.synthetic-notes", enabled: true }); }
      if (url.endsWith("/api/v2/plugins")) return json({ plugins: [enabled
        ? { id: "com.example.synthetic-notes", name: "合成备忘", version: "0.1.0", contract: "v2-preview", state: "Ready", trusted: false, loaded: true, diagnostics: [], permissions: [], uiPath: "/plugins/local/com.example.synthetic-notes" }
        : { id: "com.example.synthetic-notes", name: "合成备忘", version: "0.1.0", contract: "v2-preview", state: "Installed", trusted: false, loaded: false, diagnostics: [], permissions: [], uiPath: null }] });
      if (url.endsWith("/api/v2/diagnostics")) return json({ schema: "workbench.diagnostics.v2.preview", service: { bind: "loopback", lanEnabled: false, autoSend: false }, plugins: { discovered: 1, ready: 0, invalid: 0, loaded: 0 }, projects: { count: 2, activePresent: true }, issues: [], nativeWindow: { minimumWidth: 1100 } });
      if (url.endsWith("/api/v2/ui/theme")) return json({ schema: "workbench.ui-theme.v2.preview", schemaVersion: 2, setting: "system" });
      if (url.endsWith("/api/v2/session")) return json(null, 204);
      if (url.endsWith("/api/v2/ui/desk") && init?.method === "PUT") {
        const body = JSON.parse(String(init.body)) as { pinned: string[] };
        return json({ schema: "workbench.ui-desk.v2.preview", schemaVersion: 2, pinned: body.pinned, recent: [], recovered: false });
      }
      if (url.endsWith("/api/v2/ui/desk")) return json({ schema: "workbench.ui-desk.v2.preview", schemaVersion: 2, pinned: ["upcam"], recent: [], recovered: false });
      return json({ error: "NotFound" }, 404);
    }));
    const user = userEvent.setup();
    render(<MemoryRouter><App /></MemoryRouter>);
    await user.click(await screen.findByRole("link", { name: "市场" }));
    await user.click(screen.getByRole("link", { name: "已安装" }));
    expect(await screen.findByText("已安装，尚未启用")).toBeTruthy();
    await user.click(screen.getAllByRole("button", { name: "启用" })[0]);
    await user.click(within(screen.getByRole("dialog", { name: "启用「合成备忘」" })).getByRole("button", { name: "启用" }));
    expect((await screen.findAllByRole("button", { name: "打开" })).length).toBeGreaterThan(0);
    await user.click(screen.getByRole("button", { name: "把 合成备忘 固定到左侧" }));
    expect(await screen.findByText("「合成备忘」已固定到左侧。关掉再打开还在。")).toBeTruthy();
    await user.click(screen.getAllByRole("button", { name: "打开" })[0]);
    expect(await screen.findByText("已在这台电脑上启用。打开的是记下的清单。没有附带后端程序，也没有从网上下载。")).toBeTruthy();
  });

  it("removes a recorded plugin after confirmation and leaves the open page", async () => {
    sessionStorage.setItem("workbench.session", "preview-session");
    let present = true;
    vi.stubGlobal("WebSocket", class {
      addEventListener(type: string, handler: () => void) { if (type === "open") handler(); }
      close() { return undefined; }
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo) => {
      const url = String(input);
      if (url.endsWith("/api/v2/info")) return json({ product: "ProjectWorkbench", apiVersion: "2", preview: true, device: { id: "local", label: "本机", kind: "loopback" }, lan: { enabled: false, availableInPreview: false }, autoSend: false, catalogSchemaVersion: 1 });
      if (url.endsWith("/api/v2/projects")) return json(projects);
      if (url.endsWith("/remove")) { present = false; return json({ id: "com.example.synthetic-notes", removed: true }); }
      if (url.endsWith("/api/v2/plugins")) return json({ plugins: present ? [{ id: "com.example.synthetic-notes", name: "合成备忘", version: "0.1.0", contract: "v2-preview", state: "Ready", trusted: false, loaded: true, diagnostics: [], permissions: [], uiPath: "/plugins/local/com.example.synthetic-notes" }] : [] });
      if (url.endsWith("/api/v2/diagnostics")) return json({ schema: "workbench.diagnostics.v2.preview", service: { bind: "loopback", lanEnabled: false, autoSend: false }, plugins: { discovered: 1, ready: 1, invalid: 0, loaded: 1 }, projects: { count: 2, activePresent: true }, issues: [], nativeWindow: { minimumWidth: 1100 } });
      if (url.endsWith("/api/v2/ui/theme")) return json({ schema: "workbench.ui-theme.v2.preview", schemaVersion: 2, setting: "system" });
      if (url.endsWith("/api/v2/session")) return json(null, 204);
      if (url.endsWith("/api/v2/ui/desk")) return json({ schema: "workbench.ui-desk.v2.preview", schemaVersion: 2, pinned: ["upcam"], recent: [], recovered: false });
      return json({ error: "NotFound" }, 404);
    }));
    const user = userEvent.setup();
    render(<MemoryRouter><App /></MemoryRouter>);
    await user.click(await screen.findByRole("link", { name: "市场" }));
    await user.click(screen.getByRole("link", { name: "已安装" }));
    await user.click(screen.getAllByRole("button", { name: "打开" })[0]);
    expect(await screen.findByRole("heading", { name: "合成备忘" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "卸掉" }));
    await user.click(within(screen.getByRole("dialog", { name: "卸掉「合成备忘」" })).getByRole("button", { name: "卸掉" }));
    expect(await screen.findByRole("heading", { name: "已安装" })).toBeTruthy();
    expect(screen.queryByText("合成备忘")).toBeNull();
  });

  it("shows the missing session and offline states", async () => {
    render(<MemoryRouter><App /></MemoryRouter>);
    expect(screen.getByRole("alert").textContent).toContain("未找到本机会话");
    cleanup();
    sessionStorage.setItem("workbench.session", "preview-session");
    vi.stubGlobal("fetch", vi.fn(async () => json({ error: "Unauthorized" }, 401)));
    render(<MemoryRouter><App /></MemoryRouter>);
    expect(await screen.findByRole("heading", { name: "本地服务未连接" })).toBeTruthy();
  });
});

describe("command palette", () => {
  it("moves with the arrow keys, clears with escape, then closes", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(<MemoryRouter><CommandPalette projects={[{ id: "a", name: "星河协作" }]} activeProjectId="a" onClose={onClose} onOpenPlugin={vi.fn()} onPickDevice={vi.fn()} onSwitchProject={vi.fn()} /></MemoryRouter>);
    const search = screen.getByRole("textbox", { name: "搜索插件、项目、设备或命令" });
    await user.type(search, "{ArrowDown}{ArrowUp}");
    await user.type(search, "没有这个");
    expect(screen.getByText("没有匹配的结果。换个词，或按 Esc 清空。")).toBeTruthy();
    await user.keyboard("{Escape}");
    expect(onClose).not.toHaveBeenCalled();
    expect((search as HTMLInputElement).value).toBe("");
    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalled();
  });

  it("opens a local plugin from the keyboard and keeps the recent label", async () => {
    const user = userEvent.setup();
    const onOpen = vi.fn();
    const onClose = vi.fn();
    render(<MemoryRouter><CommandPalette
      projects={[{ id: "a", name: "星河协作" }]}
      activeProjectId="a"
      recent={["ding"]}
      plugins={[
        { id: "upcam", name: "UPCAM 站会日报", hint: "整理今日站会", loaded: true },
        { id: "ding", name: "钉钉 Insights", hint: "看本周沟通", loaded: true },
        { id: "test", name: "测试设计", hint: "用例仓库", loaded: false, state: "没能载入" },
      ]}
      onClose={onClose}
      onOpenPlugin={onOpen}
      onPickDevice={vi.fn()}
      onSwitchProject={vi.fn()}
    /></MemoryRouter>);
    const search = screen.getByRole("textbox", { name: "搜索插件、项目、设备或命令" });
    await user.type(search, "站会");
    expect(screen.getByRole("option", { name: /UPCAM 站会日报/ })).toBeTruthy();
    await user.keyboard("{Enter}");
    expect(onOpen).toHaveBeenCalledWith("upcam");
    expect(onClose).toHaveBeenCalled();
    await user.clear(search);
    expect(screen.getAllByRole("option")[0]?.textContent).toContain("钉钉 Insights");
    expect(screen.getByRole("option", { name: /钉钉 Insights/ }).textContent).toContain("最近打开");
    await user.type(search, "测试设计");
    await user.keyboard("{Enter}");
    expect(screen.getByRole("status").textContent).toContain("没能载入");
    expect(onOpen).toHaveBeenCalledTimes(1);
  });

  it("refuses a remote computer and a missing standup", async () => {
    const user = userEvent.setup();
    const onPick = vi.fn();
    const onOpen = vi.fn();
    render(<MemoryRouter><CommandPalette
      projects={[]}
      activeProjectId=""
      plugins={[{ id: "ding", name: "钉钉 Insights", loaded: true }]}
      onClose={vi.fn()}
      onOpenPlugin={onOpen}
      onPickDevice={onPick}
      onSwitchProject={vi.fn()}
    /></MemoryRouter>);
    const search = screen.getByRole("textbox", { name: "搜索插件、项目、设备或命令" });
    await user.type(search, "书房");
    await user.keyboard("{Enter}");
    expect(onPick).not.toHaveBeenCalled();
    expect(screen.getByRole("status").textContent).toContain("连不上");
    await user.clear(search);
    await user.type(search, "站会摘要");
    await user.keyboard("{Enter}");
    expect(onOpen).not.toHaveBeenCalled();
    expect(screen.getByRole("status").textContent).toContain("没有可用的站会日报");
  });
});
