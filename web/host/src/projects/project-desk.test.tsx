import { cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ProjectDeskApi, ProjectNote, ProjectRoster } from "./client";
import { ProjectDesk } from "./ProjectDesk";

const star = { id: "default", name: "星河协作", isArchived: false };
const lamp = { id: "lamp", name: "灯塔实验", isArchived: false };

function roster(active = "default", extra: ProjectRoster["projects"] = []): ProjectRoster {
  return { schemaVersion: 1, revision: "rev-1", activeProjectId: active, projects: [star, lamp, ...extra] };
}

function note(text: string, revision = "note-1"): ProjectNote {
  return { schemaVersion: 1, revision, text, missing: revision === "missing" };
}

function memoryDesk() {
  let current = roster();
  const notes = new Map<string, ProjectNote>([
    ["default", note("只在星河")],
    ["lamp", note("只在灯塔", "note-lamp")],
  ]);
  let failSave = false;
  let failOpen = false;
  let opened = true;
  const client: ProjectDeskApi = {
    list: async () => structuredClone(current),
    readNote: async () => structuredClone(notes.get(current.activeProjectId) ?? note("", "missing")),
    saveNote: async (text, expected) => {
      const saved = notes.get(current.activeProjectId) ?? note("", "missing");
      if (failSave) throw new Error("StoreIoFailure");
      if (saved.revision !== expected) throw new Error("RevisionConflict");
      const next = note(text, `${saved.revision}-2`);
      notes.set(current.activeProjectId, next);
      return next;
    },
    create: async (name, expected) => {
      if (expected !== current.revision) throw new Error("RevisionConflict");
      const id = "coast";
      const projects = [...current.projects, { id, name, isArchived: false }];
      current = opened
        ? { ...current, revision: "rev-2", activeProjectId: id, projects }
        : { ...current, revision: "rev-2", projects };
      if (!notes.has(id)) notes.set(id, note("", "missing"));
      return { ...structuredClone(current), opened };
    },
    rename: async (name, expected) => {
      if (expected !== current.revision) throw new Error("RevisionConflict");
      if (name === "拒绝") throw new Error("StoreIoFailure");
      current = {
        ...current,
        revision: "rev-3",
        projects: current.projects.map((project) => project.id === current.activeProjectId ? { ...project, name } : project),
      };
      return structuredClone(current);
    },
    open: async (id, expected) => {
      if (failOpen) throw new Error("ProjectNotFound");
      if (expected !== current.revision) throw new Error("RevisionConflict");
      current = { ...current, revision: "rev-4", activeProjectId: id };
      return structuredClone(current);
    },
  };
  return {
    client,
    notes,
    failSave: (value: boolean) => { failSave = value; },
    failOpen: (value: boolean) => { failOpen = value; },
    setOpened: (value: boolean) => { opened = value; },
    conflictNext: () => {
      const saved = notes.get(current.activeProjectId);
      if (saved) notes.set(current.activeProjectId, note(saved.text, "other"));
    },
  };
}

afterEach(() => {
  cleanup();
});

async function shown() {
  expect(await screen.findByRole("heading", { name: "项目" })).toBeTruthy();
}

describe("project desk", () => {
  it("shows the current project and refuses an empty or duplicate name", async () => {
    const desk = memoryDesk();
    const user = userEvent.setup();
    render(<ProjectDesk client={desk.client} />);
    await shown();
    expect(screen.getByRole("heading", { name: "星河协作" })).toBeTruthy();
    expect(screen.getByDisplayValue("只在星河")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "新建项目" }));
    await user.click(screen.getByRole("button", { name: "创建并打开" }));
    expect(screen.getByRole("alert").textContent).toBe("请先写一个项目名称。");
    expect(screen.getByRole("heading", { name: "星河协作" })).toBeTruthy();

    await user.type(screen.getByLabelText("项目名称"), "灯塔实验");
    expect(screen.getByRole("alert").textContent).toBe("已经有叫「灯塔实验」的项目。");
    await user.click(screen.getByRole("button", { name: "创建并打开" }));
    expect(screen.getByRole("heading", { name: "星河协作" })).toBeTruthy();
    await user.keyboard("{Escape}");
  });

  it("creates a project with an empty note and does not carry the previous note", async () => {
    const desk = memoryDesk();
    const user = userEvent.setup();
    render(<ProjectDesk client={desk.client} />);
    await shown();
    await user.click(screen.getByRole("button", { name: "新建项目" }));
    await user.type(screen.getByLabelText("项目名称"), "海岸记录");
    await user.click(screen.getByRole("button", { name: "创建并打开" }));
    expect(await screen.findByRole("heading", { name: "海岸记录" })).toBeTruthy();
    expect(screen.getByText("已新建并打开「海岸记录」。")).toBeTruthy();
    expect(screen.getByLabelText(/只保存在「海岸记录」/).textContent).toBe("");
    expect(screen.getByText("还没有备注。")).toBeTruthy();
    expect(desk.notes.get("default")?.text).toBe("只在星河");
  });

  it("keeps the current project and its note when the new project cannot be opened", async () => {
    const desk = memoryDesk();
    desk.setOpened(false);
    const user = userEvent.setup();
    render(<ProjectDesk client={desk.client} />);
    await shown();
    await user.click(screen.getByRole("button", { name: "新建项目" }));
    await user.type(screen.getByLabelText("项目名称"), "海岸记录");
    await user.click(screen.getByRole("button", { name: "创建并打开" }));
    expect(await screen.findByText("已经记下「海岸记录」，但没能打开。当前还是「星河协作」。")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "星河协作" })).toBeTruthy();
    expect(screen.getByDisplayValue("只在星河")).toBeTruthy();
    expect(screen.getByRole("button", { name: "打开「海岸记录」" })).toBeTruthy();
  });

  it("renames the current project and leaves the note there", async () => {
    const desk = memoryDesk();
    const user = userEvent.setup();
    render(<ProjectDesk client={desk.client} />);
    await shown();
    await user.click(screen.getByRole("button", { name: "重命名" }));
    const dialog = screen.getByRole("dialog", { name: "重命名当前项目" });
    const field = within(dialog).getByLabelText("项目名称");
    await user.clear(field);
    await user.type(field, "海岸记录");
    await user.click(within(dialog).getByRole("button", { name: "重命名" }));
    expect(await screen.findByRole("heading", { name: "海岸记录" })).toBeTruthy();
    expect(screen.getByDisplayValue("只在星河")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "重命名" }));
    const again = screen.getByRole("dialog", { name: "重命名当前项目" });
    await user.clear(within(again).getByLabelText("项目名称"));
    await user.type(within(again).getByLabelText("项目名称"), "拒绝");
    await user.click(within(again).getByRole("button", { name: "重命名" }));
    expect((await screen.findByRole("alert")).textContent).toBe("没能改名。当前还是「海岸记录」。");
    expect(screen.getByRole("heading", { name: "海岸记录" })).toBeTruthy();
  });

  it("does not switch projects when the note cannot be saved or the open fails", async () => {
    const desk = memoryDesk();
    const user = userEvent.setup();
    render(<ProjectDesk client={desk.client} />);
    await shown();
    desk.failSave(true);
    const field = screen.getByLabelText(/只保存在/);
    await user.clear(field);
    await user.type(field, "还没写进去");
    await user.click(screen.getByRole("button", { name: "打开「灯塔实验」" }));
    expect(await screen.findByText("备注还没保存，所以没有离开「星河协作」。")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "星河协作" })).toBeTruthy();
    expect(screen.getByDisplayValue("还没写进去")).toBeTruthy();

    desk.failSave(false);
    await user.clear(field);
    await user.type(field, "只在星河");
    await user.tab();
    desk.failOpen(true);
    await user.click(screen.getByRole("button", { name: "打开「灯塔实验」" }));
    expect(await screen.findByText("没能换到「灯塔实验」。当前还是「星河协作」。")).toBeTruthy();
    expect(screen.getByDisplayValue("只在星河")).toBeTruthy();
  });

  it("shows the other project's note after opening it", async () => {
    const desk = memoryDesk();
    const user = userEvent.setup();
    render(<ProjectDesk client={desk.client} />);
    await shown();
    await user.click(screen.getByRole("button", { name: "打开「灯塔实验」" }));
    expect(await screen.findByRole("heading", { name: "灯塔实验" })).toBeTruthy();
    expect(screen.getByDisplayValue("只在灯塔")).toBeTruthy();
    expect(screen.queryByDisplayValue("只在星河")).toBeNull();
  });

  it("keeps a conflicting draft and can save it after re-reading", async () => {
    const desk = memoryDesk();
    const user = userEvent.setup();
    render(<ProjectDesk client={desk.client} />);
    await shown();
    const field = screen.getByLabelText(/只保存在/);
    await user.clear(field);
    await user.type(field, "新的备注");
    desk.conflictNext();
    await user.tab();
    expect(await screen.findByText("备注没保存，写下的内容还在。当前项目仍是「星河协作」。")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "仍然保存输入框里的" }));
    expect(await screen.findByText("已保存在这个项目。")).toBeTruthy();
    expect(desk.notes.get("default")?.text).toBe("新的备注");
  });

  it("keeps the draft when saving over the saved note fails", async () => {
    const desk = memoryDesk();
    const user = userEvent.setup();
    render(<ProjectDesk client={desk.client} />);
    await shown();
    const field = screen.getByLabelText(/只保存在/);
    await user.clear(field);
    await user.type(field, "新的备注");
    desk.conflictNext();
    await user.tab();
    expect(await screen.findByRole("button", { name: "仍然保存输入框里的" })).toBeTruthy();
    desk.failSave(true);
    await user.click(screen.getByRole("button", { name: "仍然保存输入框里的" }));
    expect(await screen.findByText("备注没保存，写下的内容还在。当前项目仍是「星河协作」。")).toBeTruthy();
    expect(screen.getByDisplayValue("新的备注")).toBeTruthy();
    desk.client.readNote = async () => { throw new Error("ServiceFailed"); };
    await user.click(screen.getByRole("button", { name: "改回已保存的备注" }));
    expect(await screen.findByText("没能读回已保存的备注。输入框里的内容还在。当前项目仍是「星河协作」。")).toBeTruthy();
    expect(screen.getByDisplayValue("新的备注")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "星河协作" })).toBeTruthy();
  });

  it("does not create while a note is still unsaved", async () => {
    const desk = memoryDesk();
    desk.failSave(true);
    const user = userEvent.setup();
    render(<ProjectDesk client={desk.client} />);
    await shown();
    const field = screen.getByLabelText(/只保存在/);
    await user.clear(field);
    await user.type(field, "还没写进去");
    await user.click(screen.getByRole("button", { name: "新建项目" }));
    await user.type(screen.getByLabelText("项目名称"), "海岸记录");
    await user.click(screen.getByRole("button", { name: "创建并打开" }));
    expect((await screen.findByRole("alert")).textContent).toBe("备注还没保存，所以没有继续。当前还是「星河协作」。");
    expect(screen.getByRole("heading", { name: "星河协作" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "打开「海岸记录」" })).toBeNull();
  });

  it("keeps a conflicting draft until the user chooses", async () => {
    const desk = memoryDesk();
    const user = userEvent.setup();
    render(<ProjectDesk client={desk.client} />);
    await shown();
    const field = screen.getByLabelText(/只保存在/);
    await user.clear(field);
    await user.type(field, "新的备注");
    desk.conflictNext();
    await user.tab();
    expect(await screen.findByText("备注没保存，写下的内容还在。当前项目仍是「星河协作」。")).toBeTruthy();
    expect(screen.getByDisplayValue("新的备注")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "改回已保存的备注" }));
    expect(await screen.findByDisplayValue("只在星河")).toBeTruthy();
  });

  it("reads the same project again after the page is opened a second time", async () => {
    const desk = memoryDesk();
    const user = userEvent.setup();
    const first = render(<ProjectDesk client={desk.client} />);
    await shown();
    await user.click(screen.getByRole("button", { name: "重命名" }));
    const dialog = screen.getByRole("dialog", { name: "重命名当前项目" });
    await user.clear(within(dialog).getByLabelText("项目名称"));
    await user.type(within(dialog).getByLabelText("项目名称"), "海岸记录");
    await user.click(within(dialog).getByRole("button", { name: "重命名" }));
    expect(await screen.findByRole("heading", { name: "海岸记录" })).toBeTruthy();
    first.unmount();
    render(<ProjectDesk client={desk.client} />);
    expect(await screen.findByRole("heading", { name: "海岸记录" })).toBeTruthy();
    expect(screen.getByDisplayValue("只在星河")).toBeTruthy();
  });

  it("offers another try when the list cannot be read", async () => {
    const list = vi.fn<() => Promise<ProjectRoster>>()
      .mockRejectedValueOnce(new Error("ServiceFailed"))
      .mockResolvedValue(roster());
    const client: ProjectDeskApi = {
      list,
      readNote: async () => note("只在星河"),
      saveNote: async () => note("只在星河"),
      create: async () => ({ ...roster(), opened: true }),
      rename: async () => roster(),
      open: async () => roster(),
    };
    const user = userEvent.setup();
    render(<ProjectDesk client={client} />);
    expect((await screen.findByRole("alert")).textContent).toBe("没能读到项目。请再试一次。");
    await user.click(screen.getByRole("button", { name: "再试一次" }));
    expect(await screen.findByRole("heading", { name: "星河协作" })).toBeTruthy();
  });
});
