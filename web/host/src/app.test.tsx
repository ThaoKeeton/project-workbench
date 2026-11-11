import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

const projects = {
  schemaVersion: 1,
  revision: "rev-1",
  activeProjectId: "default",
  projects: [
    { id: "default", name: "默认示例", isArchived: false },
    { id: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", name: "示例项目", isArchived: false },
  ],
};

function json(body: unknown, status = 200): Response {
  return new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

afterEach(() => {
  cleanup();
  localStorage.clear();
  sessionStorage.clear();
  vi.unstubAllGlobals();
});

describe("app shell", () => {
  it("loads the local overview and can search", async () => {
    sessionStorage.setItem("workbench.session", "preview-session");
    Object.defineProperty(window, "matchMedia", {
      writable: true,
      value: (query: string) => ({
        matches: query.includes("959"),
        media: query,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
      }),
    });
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
      return json({ error: "NotFound" }, 404);
    }));
    const user = userEvent.setup();
    render(<MemoryRouter><App /></MemoryRouter>);
    expect(await screen.findByRole("heading", { name: "今天的工作台" })).toBeTruthy();
    await user.type(screen.getByRole("textbox", { name: "搜索命令、插件或设备" }), "诊断");
    await user.click(screen.getAllByRole("button", { name: "打开诊断" })[0]);
    await user.click(screen.getByRole("button", { name: "当前设备：本机" }));
    expect(document.body.textContent).toContain("局域网未连接");
  });

  it("shows the missing session message", () => {
    render(<MemoryRouter><App /></MemoryRouter>);
    expect(screen.getByRole("alert").textContent).toContain("未找到本机会话");
  });
});
