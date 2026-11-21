import { screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
}

afterEach(() => {
  document.body.innerHTML = "";
  sessionStorage.clear();
  vi.unstubAllGlobals();
});

describe("project desk boot", () => {
  it("mounts the desk from the page root", async () => {
    document.body.innerHTML = '<div id="root"></div>';
    sessionStorage.setItem("workbench.session", "preview");
    vi.stubGlobal("matchMedia", () => ({ matches: false, addEventListener: () => undefined, removeEventListener: () => undefined }));
    vi.stubGlobal("fetch", vi.fn(async (url: string) => {
      if (String(url).endsWith("/context")) return json({ schemaVersion: 1, revision: "missing", text: "", missing: true });
      return json({
        schemaVersion: 1,
        revision: "rev-1",
        activeProjectId: "default",
        projects: [{ id: "default", name: "星河协作", isArchived: false }],
      });
    }));
    await import("./boot");
    expect(await screen.findByRole("heading", { name: "星河协作" })).toBeTruthy();
  });
});
