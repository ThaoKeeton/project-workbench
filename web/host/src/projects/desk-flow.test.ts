import { describe, expect, it, vi } from "vitest";
import type { ProjectDeskApi, ProjectNote, ProjectRoster } from "./client";
import { createProject, openProject, renameProject, saveDraft, saveDraftOver } from "./desk-flow";

const roster: ProjectRoster = {
  schemaVersion: 1,
  revision: "rev-1",
  activeProjectId: "default",
  projects: [
    { id: "default", name: "星河协作", isArchived: false },
    { id: "lamp", name: "灯塔实验", isArchived: false },
  ],
};

const note: ProjectNote = { schemaVersion: 1, revision: "note-1", text: "只在星河", missing: false };

function client(partial: Partial<ProjectDeskApi>): ProjectDeskApi {
  return {
    list: vi.fn(async () => roster),
    create: vi.fn(async () => ({ ...roster, opened: true })),
    rename: vi.fn(async () => roster),
    open: vi.fn(async () => roster),
    readNote: vi.fn(async () => note),
    saveNote: vi.fn(async () => note),
    ...partial,
  };
}

describe("project desk flow", () => {
  it("does not create or rename when the name is empty or already used", async () => {
    const api = client({});
    expect((await createProject(api, roster, "  ")).type).toBe("rejected");
    expect((await createProject(api, roster, "灯塔实验")).type).toBe("rejected");
    expect((await renameProject(api, roster, "灯塔实验")).type).toBe("rejected");
    expect(api.create).not.toHaveBeenCalled();
    expect(api.rename).not.toHaveBeenCalled();
  });

  it("keeps the current project when opening the new one fails", async () => {
    const created = {
      ...roster,
      revision: "rev-2",
      projects: [...roster.projects, { id: "coast", name: "海岸记录", isArchived: false }],
      opened: false,
    };
    const api = client({ create: vi.fn(async () => created) });
    const outcome = await createProject(api, roster, "海岸记录");
    expect(outcome).toMatchObject({ type: "kept", notice: "已经记下「海岸记录」，但没能打开。当前还是「星河协作」。" });
  });

  it("opens the new project with its own empty note", async () => {
    const created = {
      schemaVersion: 1,
      revision: "rev-2",
      activeProjectId: "coast",
      projects: [...roster.projects, { id: "coast", name: "海岸记录", isArchived: false }],
      opened: true,
    };
    const empty = { schemaVersion: 1, revision: "missing", text: "", missing: true };
    const api = client({
      create: vi.fn(async () => created),
      readNote: vi.fn(async () => empty),
    });
    const outcome = await createProject(api, roster, " 海岸记录 ");
    expect(outcome).toMatchObject({ type: "opened", note: empty, notice: "已新建并打开「海岸记录」。" });
  });

  it("uses the service wording when the saved list rejects a name the page allowed", async () => {
    const duplicate = client({ create: vi.fn(async () => { throw new Error("DuplicateName"); }) });
    expect(await createProject(duplicate, roster, "海岸记录")).toMatchObject({ type: "rejected", message: "已经有叫「海岸记录」的项目。" });
    const invalid = client({ create: vi.fn(async () => { throw new Error("InvalidName"); }) });
    expect(await createProject(invalid, roster, "海岸记录")).toMatchObject({ type: "rejected", message: "这个名称用不了。请换成没有换行、80 个字以内的名称。" });
    const known = client({ rename: vi.fn(async () => { throw new Error("DuplicateName"); }) });
    expect(await renameProject(known, roster, "海岸记录")).toMatchObject({ type: "rejected", message: "已经有叫「海岸记录」的项目。" });
  });

  it("reloads after a conflict and still reports a blocked create", async () => {
    const api = client({
      create: vi.fn(async () => { throw new Error("RevisionConflict"); }),
      list: vi.fn(async () => ({ ...roster, revision: "rev-9" })),
    });
    const conflict = await createProject(api, roster, "海岸记录");
    expect(conflict).toMatchObject({ type: "rejected", message: "项目列表刚被更新，已重新读取。请再试一次。", roster: { revision: "rev-9" } });

    const blocked = client({
      create: vi.fn(async () => { throw new Error("ServiceFailed"); }),
      list: vi.fn(async () => { throw new Error("ServiceFailed"); }),
    });
    expect(await createProject(blocked, roster, "海岸记录")).toMatchObject({ type: "blocked", message: "没能新建。当前还是「星河协作」。" });

    const unchanged = client({
      create: vi.fn(async () => { throw new Error("ServiceFailed"); }),
      list: vi.fn(async () => roster),
    });
    expect(await createProject(unchanged, roster, "海岸记录")).toMatchObject({ type: "blocked", message: "没能新建。当前还是「星河协作」。" });

    const lostList = client({
      create: vi.fn(async () => { throw new Error("RevisionConflict"); }),
      list: vi.fn(async () => { throw new Error("ServiceFailed"); }),
    });
    expect(await createProject(lostList, roster, "海岸记录")).toMatchObject({ type: "rejected", roster: undefined });
  });

  it("recovers a project that was stored even though the response failed", async () => {
    const stored = {
      ...roster,
      revision: "rev-4",
      projects: [...roster.projects, { id: "coast", name: "海岸记录", isArchived: false }],
    };
    const api = client({
      create: vi.fn(async () => { throw new Error("ServiceFailed"); }),
      list: vi.fn(async () => stored),
    });
    expect(await createProject(api, roster, "海岸记录")).toMatchObject({ type: "kept", roster: stored });

    const switched = { ...stored, activeProjectId: "coast" };
    const switchedApi = client({
      create: vi.fn(async () => { throw new Error("ServiceFailed"); }),
      list: vi.fn(async () => switched),
      readNote: vi.fn(async () => { throw new Error("ServiceFailed"); }),
    });
    const outcome = await createProject(switchedApi, roster, "海岸记录");
    expect(outcome).toMatchObject({ type: "opened", noteUnread: true, roster: switched });
  });

  it("renames in place and refuses a failure without swapping the name", async () => {
    const renamed = { ...roster, revision: "rev-2", projects: roster.projects.map((project) => project.id === "default" ? { ...project, name: "海岸记录" } : project) };
    const api = client({ rename: vi.fn(async () => renamed) });
    expect(await renameProject(api, roster, "海岸记录")).toMatchObject({ type: "renamed", notice: "已改名为「海岸记录」。" });
    expect(await renameProject(client({ rename: vi.fn(async () => roster) }), roster, "星河协作")).toMatchObject({ type: "renamed", notice: "现在的名字就是「星河协作」。" });
    const failed = client({ rename: vi.fn(async () => { throw new Error("StoreIoFailure"); }) });
    expect(await renameProject(failed, roster, "海岸记录")).toMatchObject({ type: "blocked", message: "没能改名。当前还是「星河协作」。" });
  });

  it("keeps the current project when a switch fails and reads the destination note when it works", async () => {
    expect(await openProject(client({}), roster, "default")).toMatchObject({ type: "same" });
    const lamp = { ...roster, revision: "rev-2", activeProjectId: "lamp" };
    const lampNote = { schemaVersion: 1, revision: "missing", text: "", missing: true };
    const opened = await openProject(client({ open: vi.fn(async () => lamp), readNote: vi.fn(async () => lampNote) }), roster, "lamp");
    expect(opened).toMatchObject({ type: "opened", note: lampNote, notice: "已换到「灯塔实验」。" });

    const denied = client({ open: vi.fn(async () => { throw new Error("ProjectNotFound"); }), list: vi.fn(async () => roster) });
    expect(await openProject(denied, roster, "lamp")).toMatchObject({ type: "blocked", message: "没能换到「灯塔实验」。当前还是「星河协作」。" });

    const stale = client({
      open: vi.fn(async () => { throw new Error("RevisionConflict"); }),
      list: vi.fn(async () => ({ ...roster, revision: "rev-8" })),
    });
    expect(await openProject(stale, roster, "lamp")).toMatchObject({ type: "rejected", roster: { revision: "rev-8" } });

    const moved = client({
      open: vi.fn(async () => { throw new Error("ServiceFailed"); }),
      list: vi.fn(async () => ({ ...roster, revision: "rev-5" })),
    });
    expect(await openProject(moved, roster, "lamp")).toMatchObject({ type: "rejected", message: "项目列表刚被更新，已重新读取。请再选一次。" });
    const renamed = client({ rename: vi.fn(async () => { throw new Error("RevisionConflict"); }), list: vi.fn(async () => ({ ...roster, revision: "rev-6" })) });
    expect(await renameProject(renamed, roster, "海岸记录")).toMatchObject({ type: "rejected", roster: { revision: "rev-6" } });
  });

  it("saves a note, keeps a conflict, and can overwrite after re-reading", async () => {
    expect(await saveDraft(client({}), note, "只在星河")).toMatchObject({ type: "clean" });
    const saved = { ...note, revision: "note-2", text: "改过" };
    expect(await saveDraft(client({ saveNote: vi.fn(async () => saved) }), note, "改过")).toMatchObject({ type: "saved", note: saved });
    expect(await saveDraft(client({ saveNote: vi.fn(async () => { throw new Error("RevisionConflict"); }) }), note, "改过")).toMatchObject({ type: "conflict" });
    expect(await saveDraft(client({ saveNote: vi.fn(async () => { throw new Error("StoreIoFailure"); }) }), note, "改过")).toMatchObject({ type: "failed" });
    const overwritten = await saveDraftOver(client({
      readNote: vi.fn(async () => ({ ...note, revision: "note-9" })),
      saveNote: vi.fn(async () => ({ ...note, revision: "note-10", text: "改过" })),
    }), "改过");
    expect(overwritten).toMatchObject({ type: "saved", note: { text: "改过" } });
    expect(await saveDraftOver(client({ readNote: vi.fn(async () => { throw new Error("InvalidContext"); }) }), "改过")).toMatchObject({ type: "failed" });
  });
});
