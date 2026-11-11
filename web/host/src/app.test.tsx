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
    expect(await screen.findByRole("heading", { name: "早上好，文前" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: /星河笔记本/ }));
    expect(screen.getByText("书房工作站")).toBeTruthy();
    await user.click(screen.getByRole("menuitem", { name: /书房工作站/ }));
    await user.keyboard("{Control>}k{/Control}");
    const search = await screen.findByRole("textbox", { name: "搜索插件、命令或设备" });
    await user.type(search, "市场");
    await user.keyboard("{Enter}");
    expect(await screen.findByRole("heading", { name: "插件市场" })).toBeTruthy();
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
  it("moves with the arrow keys and reports an empty filter", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(<MemoryRouter><CommandPalette projects={[{ id: "a", name: "星河协作" }]} activeProjectId="a" onClose={onClose} onOpenPlugin={vi.fn()} onPickDevice={vi.fn()} onSwitchProject={vi.fn()} /></MemoryRouter>);
    const search = screen.getByRole("textbox", { name: "搜索插件、命令或设备" });
    await user.type(search, "{ArrowDown}{ArrowUp}");
    await user.type(search, "没有这个");
    expect(screen.getByText("没有匹配的结果")).toBeTruthy();
    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalled();
  });
});
