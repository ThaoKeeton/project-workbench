import type { CreatedProject, ProjectDeskApi, ProjectNote, ProjectRoster } from "./client";
import { activeName, nameFeedback } from "./names";

export function codeOf(error: unknown): string {
  return error instanceof Error ? error.message : "";
}

export function emptyNote(): ProjectNote {
  return { schemaVersion: 1, revision: "missing", text: "", missing: true };
}

export type NoteSave =
  | { type: "clean" }
  | { type: "saved"; note: ProjectNote }
  | { type: "conflict" }
  | { type: "failed" };

export async function saveDraft(client: ProjectDeskApi, note: ProjectNote, draft: string, projectId: string): Promise<NoteSave> {
  if (draft === note.text) return { type: "clean" };
  try {
    return { type: "saved", note: await client.saveNote(draft, note.revision, projectId) };
  } catch (error) {
    return { type: codeOf(error) === "RevisionConflict" ? "conflict" : "failed" };
  }
}

export async function saveDraftOver(client: ProjectDeskApi, draft: string, projectId: string): Promise<NoteSave> {
  try {
    const current = await client.readNote();
    return { type: "saved", note: await client.saveNote(draft, current.revision, projectId) };
  } catch (error) {
    return { type: codeOf(error) === "RevisionConflict" ? "conflict" : "failed" };
  }
}

export type Rejected = { type: "rejected"; message: string; roster?: ProjectRoster };
export type Blocked = { type: "blocked"; message: string };

export type CreateOutcome =
  | { type: "opened"; roster: ProjectRoster; note: ProjectNote; notice: string; noteUnread: boolean }
  | { type: "kept"; roster: ProjectRoster; notice: string }
  | Rejected
  | Blocked;

export async function createProject(client: ProjectDeskApi, roster: ProjectRoster, typed: string): Promise<CreateOutcome> {
  const problem = nameFeedback(typed, roster.projects);
  if (problem) return { type: "rejected", message: problem };
  const current = activeName(roster.projects, roster.activeProjectId);
  try {
    const created = await client.create(typed.trim(), roster.revision);
    return await interpretCreate(client, roster, created, typed.trim(), current);
  } catch (error) {
    return recoverCreate(client, roster, typed, codeOf(error), current);
  }
}

export type RenameOutcome = { type: "renamed"; roster: ProjectRoster; notice: string } | Rejected | Blocked;

export async function renameProject(client: ProjectDeskApi, roster: ProjectRoster, typed: string): Promise<RenameOutcome> {
  const problem = nameFeedback(typed, roster.projects, roster.activeProjectId);
  if (problem) return { type: "rejected", message: problem };
  const current = activeName(roster.projects, roster.activeProjectId);
  try {
    const renamed = await client.rename(typed.trim(), roster.revision);
    const notice = renamed.revision === roster.revision
      ? `现在的名字就是「${activeName(renamed.projects, renamed.activeProjectId)}」。`
      : `已改名为「${activeName(renamed.projects, renamed.activeProjectId)}」。`;
    return { type: "renamed", roster: renamed, notice };
  } catch (error) {
    return recoverRename(client, roster, typed, codeOf(error), current);
  }
}

export type OpenOutcome =
  | { type: "same" }
  | { type: "opened"; roster: ProjectRoster; note: ProjectNote; notice: string; noteUnread: boolean }
  | Rejected
  | Blocked;

export async function openProject(client: ProjectDeskApi, roster: ProjectRoster, id: string): Promise<OpenOutcome> {
  if (id === roster.activeProjectId) return { type: "same" };
  const current = activeName(roster.projects, roster.activeProjectId);
  const target = roster.projects.find((project) => project.id === id)?.name ?? "那个项目";
  try {
    const opened = await client.open(id, roster.revision);
    if (opened.activeProjectId !== id) return { type: "blocked", message: stayPut(target, current) };
    return await readOpened(client, opened, `已换到「${activeName(opened.projects, opened.activeProjectId)}」。`);
  } catch (error) {
    return recoverOpen(client, roster, id, codeOf(error), target, current);
  }
}

async function interpretCreate(
  client: ProjectDeskApi,
  roster: ProjectRoster,
  created: CreatedProject,
  typed: string,
  current: string,
): Promise<CreateOutcome> {
  if (created.opened && created.activeProjectId !== roster.activeProjectId) {
    return readOpened(client, created, `已新建并打开「${activeName(created.projects, created.activeProjectId)}」。`);
  }
  const added = created.projects.find((project) => !roster.projects.some((item) => item.id === project.id));
  return { type: "kept", roster: created, notice: keptNotice(added?.name ?? typed, current) };
}

async function recoverCreate(
  client: ProjectDeskApi,
  roster: ProjectRoster,
  typed: string,
  code: string,
  current: string,
): Promise<CreateOutcome> {
  if (code === "RevisionConflict") return reloadRejection(client, "项目列表刚被更新，已重新读取。请再试一次。");
  if (code === "InvalidName" || code === "DuplicateName") return { type: "rejected", message: serverNameMessage(code, typed, roster) };
  const latest = await client.list().catch(() => null);
  if (!latest) return { type: "blocked", message: `没能新建。当前还是「${current}」。` };
  if (latest.activeProjectId !== roster.activeProjectId) {
    return readOpened(client, latest, `已新建并打开「${activeName(latest.projects, latest.activeProjectId)}」。`);
  }
  const added = latest.projects.find((project) => !roster.projects.some((item) => item.id === project.id));
  if (added) return { type: "kept", roster: latest, notice: keptNotice(added.name, current) };
  return { type: "blocked", message: `没能新建。当前还是「${current}」。` };
}

async function recoverRename(
  client: ProjectDeskApi,
  roster: ProjectRoster,
  typed: string,
  code: string,
  current: string,
): Promise<RenameOutcome> {
  if (code === "RevisionConflict") return reloadRejection(client, "项目列表刚被更新，已重新读取。请再试一次。");
  if (code === "InvalidName" || code === "DuplicateName") return { type: "rejected", message: serverNameMessage(code, typed, roster) };
  return { type: "blocked", message: `没能改名。当前还是「${current}」。` };
}

async function recoverOpen(
  client: ProjectDeskApi,
  roster: ProjectRoster,
  id: string,
  code: string,
  target: string,
  current: string,
): Promise<OpenOutcome> {
  if (code === "RevisionConflict") return reloadRejection(client, "项目列表刚被更新，已重新读取。请再选一次。");
  const latest = await client.list().catch(() => null);
  if (latest?.activeProjectId === id) return readOpened(client, latest, `已换到「${activeName(latest.projects, latest.activeProjectId)}」。`);
  if (latest && latest.revision !== roster.revision) {
    return { type: "rejected", message: "项目列表刚被更新，已重新读取。请再选一次。", roster: latest };
  }
  return { type: "blocked", message: stayPut(target, current) };
}

async function readOpened(client: ProjectDeskApi, roster: ProjectRoster, notice: string): Promise<Extract<CreateOutcome, { type: "opened" }>> {
  try {
    const note = await client.readNote();
    return { type: "opened", roster, note, notice, noteUnread: false };
  } catch {
    return { type: "opened", roster, note: emptyNote(), notice: `${notice.replace(/。$/, "")}。备注暂时没读到。`, noteUnread: true };
  }
}

async function reloadRejection(client: ProjectDeskApi, message: string): Promise<Rejected> {
  const latest = await client.list().catch(() => null);
  return { type: "rejected", message, roster: latest ?? undefined };
}

function serverNameMessage(code: string, typed: string, roster: ProjectRoster): string {
  if (code === "DuplicateName") {
    const taken = roster.projects.find((project) => project.name.toLocaleLowerCase("en-US") === typed.trim().toLocaleLowerCase("en-US"));
    return `已经有叫「${taken?.name ?? typed.trim()}」的项目。`;
  }
  const local = nameFeedback(typed, []);
  if (local) return local;
  return "这个名称用不了。请换成没有换行、80 个字以内的名称。";
}

function keptNotice(label: string, current: string): string {
  return `已经记下「${label}」，但没能打开。当前还是「${current}」。`;
}

function stayPut(target: string, current: string): string {
  return `没能换到「${target}」。当前还是「${current}」。`;
}
