import { afterEach, describe, expect, it } from "vitest";
import { applyTheme, readStoredSetting, resolveTheme } from "./theme";

Object.defineProperty(window, "matchMedia", {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }),
});

afterEach(() => {
  localStorage.clear();
  document.documentElement.dataset.theme = "";
  document.documentElement.dataset.themeSetting = "";
});

describe("theme allowlist", () => {
  it("rejects stored values outside system, light, and dark", () => {
    localStorage.setItem("workbench.theme", "neon");
    expect(readStoredSetting()).toBe("system");
    localStorage.setItem("workbench.theme", "dark");
    expect(readStoredSetting()).toBe("dark");
  });

  it("writes only an allowlisted literal", () => {
    applyTheme("dark");
    expect(localStorage.getItem("workbench.theme")).toBe("dark");
    expect(document.documentElement.dataset.theme).toBe("dark");
    applyTheme("light");
    expect(localStorage.getItem("workbench.theme")).toBe("light");
    expect(document.documentElement.dataset.theme).toBe("light");
    applyTheme("system");
    expect(localStorage.getItem("workbench.theme")).toBe("system");
    expect(["light", "dark"]).toContain(document.documentElement.dataset.theme);
  });

  it("resolves an explicit choice without reading storage", () => {
    expect(resolveTheme("light")).toBe("light");
    expect(resolveTheme("dark")).toBe("dark");
    expect(["light", "dark"]).toContain(resolveTheme("system"));
  });
});
