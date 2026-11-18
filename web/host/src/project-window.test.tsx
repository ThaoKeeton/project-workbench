import { cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

const projects = {
  schemaVersion: 1,
  revision: "rev-1",
  activeProjectId: "default",
  projects: [
    { id: "default", name: "星河协作", isArchived: false },
    { id: "lamp", name: "灯塔实验", isArchived: false },
  ],
};

function json(body: unknown, status = 200): Response {
  return new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

afterEach(() => {
  cleanup();
  sessionStorage.clear();
  vi.unstubAllGlobals();
});

describe("project page in the main window", () => {
  it("opens from the project list and keeps the current name when renaming fails", async () => {
    sessionStorage.setItem("workbench.session", "preview-session");
    vi.stubGlobal("matchMedia", () => ({ matches: false, media: "", addEventListener: () => undefined, removeEventListener: () => undefined }));
    vi.stubGlobal("WebSocket", class {
      addEventListener() { return undefined; }
      close() { return undefined; }
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo) => {
      const url = String(input);
      if (url.endsWith("/api/v2/info")) return json({ product: "ProjectWorkbench", apiVersion: "2", preview: true, device: { id: "local", label: "本机", kind: "loopback" }, lan: { enabled: false, availableInPreview: false }, autoSend: false, catalogSchemaVersion: 1 });
      if (url.endsWith("/api/v2/projects/name")) return json({ error: "DuplicateName" }, 409);
      if (url.endsWith("/api/v2/projects")) return json(projects);
      if (url.endsWith("/api/v2/plugins")) return json({ plugins: [] });
      if (url.endsWith("/api/v2/diagnostics")) return json({ schema: "workbench.diagnostics.v2.preview", service: { bind: "loopback", lanEnabled: false, autoSend: false }, plugins: { discovered: 0, ready: 0, invalid: 0, loaded: 0 }, projects: { count: 2, activePresent: true }, issues: [], nativeWindow: { minimumWidth: 1100 } });
      if (url.endsWith("/api/v2/ui/theme")) return json({ schema: "workbench.ui-theme.v2.preview", schemaVersion: 2, setting: "system" });
      if (url.endsWith("/api/v2/ui/desk")) return json({ pinned: [], recent: [], recovered: false });
      if (url.endsWith("/api/v2/session")) return json(null, 204);
      if (url.endsWith("/context")) return json({ schemaVersion: 1, revision: "note-1", text: "只在星河", missing: false });
      return json({ error: "NotFound" }, 404);
    }));
    const user = userEvent.setup();
    render(<MemoryRouter><App /></MemoryRouter>);
    await user.click(await screen.findByRole("button", { name: /当前项目 星河协作/ }));
    await user.click(screen.getByRole("menuitem", { name: "新建或改名" }));
    expect(await screen.findByRole("heading", { name: "项目" })).toBeTruthy();
    expect(screen.getByDisplayValue("只在星河")).toBeTruthy();
    expect(screen.getByRole("button", { name: "返回首页" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "重命名" }));
    const dialog = screen.getByRole("dialog", { name: "重命名当前项目" });
    await user.clear(within(dialog).getByLabelText("项目名称"));
    await user.type(within(dialog).getByLabelText("项目名称"), "海岸记录");
    await user.click(within(dialog).getByRole("button", { name: "重命名" }));
    expect((await screen.findByRole("alert")).textContent).toContain("海岸记录");
    expect(screen.getByRole("button", { name: /当前项目 星河协作/ })).toBeTruthy();
    expect(screen.getByDisplayValue("只在星河")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "返回首页" }));
    expect(await screen.findByRole("button", { name: /搜索插件、项目、设备或命令/ })).toBeTruthy();
  });
});
