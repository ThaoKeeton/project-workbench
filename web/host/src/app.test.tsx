import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { CommandPalette } from "./launcher";

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
    expect(await screen.findByRole("heading", { name: "这台电脑上的插件" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "UPCAM 站会日报" }));
    expect(await screen.findByRole("dialog", { name: "远程操作确认" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "确认并继续" }));
    expect(await screen.findByRole("heading", { name: "站会日报" })).toBeTruthy();
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
