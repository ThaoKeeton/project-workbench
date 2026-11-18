import { afterEach, describe, expect, it, vi } from "vitest";
import { ProjectDeskClient } from "./client";

afterEach(() => {
  vi.unstubAllGlobals();
});

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("project desk client", () => {
  it("sends the session and reads a created project", async () => {
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => {
      expect(new Headers(init?.headers).get("Authorization")).toBe("Bearer preview");
      return json({
        schemaVersion: 1,
        revision: "rev-2",
        activeProjectId: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        projects: [{ id: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", name: "海岸记录", isArchived: false }],
        opened: true,
      });
    });
    vi.stubGlobal("fetch", fetchMock);
    const client = new ProjectDeskClient("preview");
    const created = await client.create("海岸记录", "rev-1");
    expect(created.opened).toBe(true);
    expect(fetchMock).toHaveBeenCalledWith("/api/v2/projects", expect.objectContaining({ method: "POST" }));
  });

  it("uses the service error code and refuses a create response without opened", async () => {
    vi.stubGlobal("fetch", vi.fn(async (url: string) => {
      if (String(url).endsWith("/name")) return json({ error: "DuplicateName" }, 409);
      if (String(url).endsWith("/context")) return new Response("nope", { status: 500 });
      return json({ schemaVersion: 1, revision: "rev", activeProjectId: "default", projects: [] });
    }));
    const client = new ProjectDeskClient("preview");
    await expect(client.rename("灯塔实验", "rev")).rejects.toThrow("DuplicateName");
    await expect(client.readNote()).rejects.toThrow("ServiceFailed");
    await expect(client.create("海岸记录", "rev")).rejects.toThrow("ServiceFailed");
    vi.stubGlobal("fetch", vi.fn(async () => json({}, 500)));
    await expect(new ProjectDeskClient("preview").list()).rejects.toThrow("ServiceFailed");
  });

  it("calls the existing open and note routes", async () => {
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === "PUT") return json({ schemaVersion: 1, revision: "note-2", text: "只在这里", missing: false });
      if (String(url).endsWith("/active")) return json({ schemaVersion: 1, revision: "rev-3", activeProjectId: "lamp", projects: [] });
      return json({ schemaVersion: 1, revision: "rev-1", activeProjectId: "default", projects: [] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const client = new ProjectDeskClient("preview");
    expect((await client.list()).activeProjectId).toBe("default");
    expect((await client.open("lamp", "rev-1")).activeProjectId).toBe("lamp");
    expect((await client.saveNote("只在这里", "missing")).text).toBe("只在这里");
  });
});
