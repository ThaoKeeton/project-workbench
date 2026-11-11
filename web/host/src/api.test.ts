import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiClient, readSessionToken } from "./api";

afterEach(() => {
  vi.unstubAllGlobals();
  sessionStorage.clear();
});

describe("api client", () => {
  it("sends the session and reads the preview endpoints", async () => {
    vi.stubGlobal("fetch", vi.fn(async (_input: RequestInfo, init?: RequestInit) => {
      const body = init?.body ? JSON.parse(String(init.body)) : null;
      const headers = new Headers(init?.headers);
      expect(headers.get("Authorization")).toBe("Bearer preview-session");
      if (init?.method === "POST" && String(_input).endsWith("/export")) return new Response("suite", { status: 200 });
      if (init?.method === "POST" && String(_input).endsWith("/session")) return new Response(null, { status: 204 });
      if (init?.method === "PUT") return new Response(JSON.stringify(body ?? { setting: "dark" }), { status: 200 });
      if (String(_input).endsWith("/plugins")) return new Response(JSON.stringify({ plugins: [{ id: "sample" }] }), { status: 200 });
      if (String(_input).endsWith("/theme")) return new Response(JSON.stringify({ setting: "nope" }), { status: 200 });
      return new Response(JSON.stringify({ ok: true, id: body?.id ?? "default" }), { status: 200 });
    }));
    const client = new ApiClient("preview-session");
    expect(client.tokenPresent).toBe(true);
    await client.info();
    await client.projects();
    await client.switchProject("default", "rev-1");
    expect((await client.plugins())[0].id).toBe("sample");
    await client.diagnostics();
    await client.cases();
    await client.saveCases({ schema: "workbench.test-cases.v2.preview", schemaVersion: 2, suiteName: "本地", rows: [] });
    expect(await (await client.exportCases()).text()).toBe("suite");
    expect(await client.openSession()).toBe(true);
    expect(await client.theme()).toBe("system");
    await client.saveTheme("dark");
  });

  it("reports an error code and a non-json failure", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ error: "Unauthorized" }), { status: 401 })));
    await expect(new ApiClient("preview-session").info()).rejects.toThrow("Unauthorized");
    vi.stubGlobal("fetch", vi.fn(async () => new Response("nope", { status: 500 })));
    await expect(new ApiClient("x").diagnostics()).rejects.toThrow("500");
    expect(readSessionToken()).toBe("");
  });
});
