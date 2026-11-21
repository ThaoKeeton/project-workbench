import { cleanup, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mountProjectDesk } from "./mount";

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
}

afterEach(() => {
  cleanup();
  document.body.innerHTML = "";
  sessionStorage.clear();
  localStorage.clear();
  vi.unstubAllGlobals();
});

describe("project desk mount", () => {
  it("asks for the shell session before reading projects", async () => {
    vi.stubGlobal("matchMedia", () => ({ matches: false, addEventListener: () => undefined, removeEventListener: () => undefined }));
    const root = document.createElement("div");
    document.body.append(root);
    const view = mountProjectDesk(root);
    expect((await screen.findByRole("alert")).textContent).toBe("未找到本机会话。请从工作台外壳打开。");
    view.unmount();
  });

  it("opens the desk when a session is already stored", async () => {
    vi.stubGlobal("matchMedia", () => ({ matches: true, addEventListener: () => undefined, removeEventListener: () => undefined }));
    sessionStorage.setItem("workbench.session", "preview");
    localStorage.setItem("workbench.theme", "dark");
    vi.stubGlobal("fetch", vi.fn(async (url: string) => {
      if (String(url).endsWith("/context")) return json({ schemaVersion: 1, revision: "missing", text: "", missing: true });
      return json({
        schemaVersion: 1,
        revision: "rev-1",
        activeProjectId: "default",
        projects: [{ id: "default", name: "星河协作", isArchived: false }],
      });
    }));
    const root = document.createElement("div");
    document.body.append(root);
    const view = mountProjectDesk(root);
    expect(await screen.findByRole("heading", { name: "星河协作" })).toBeTruthy();
    expect(document.documentElement.dataset.theme).toBe("dark");
    view.unmount();
  });
});
