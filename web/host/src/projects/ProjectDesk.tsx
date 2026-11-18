import { useEffect, useRef, useState } from "react";
import type { ProjectDeskApi, ProjectNote, ProjectRoster } from "./client";
import { createProject, openProject, renameProject, saveDraft, saveDraftOver, type CreateOutcome, type OpenOutcome } from "./desk-flow";
import { activeName } from "./names";
import { NameDialog } from "./NameDialog";
import "./project-desk.css";

/**
 * Standalone project desk. It is not mounted from the launcher or the top bar.
 * Later integration is one route; leave launcher.tsx and shell.tsx unchanged:
 *   <Route path="/projects" element={<ProjectDesk client={new ProjectDeskClient(token)} onChanged={syncRoster} />} />
 * A link to /projects can be added by the top-bar work. This module does not add it.
 */
export function ProjectDesk({ client, onChanged }: { client: ProjectDeskApi; onChanged?: (roster: ProjectRoster) => void }) {
  const [attempt, setAttempt] = useState(0);
  const [roster, setRoster] = useState<ProjectRoster | null>(null);
  const [note, setNote] = useState<ProjectNote | null>(null);
  const [draft, setDraft] = useState("");
  const [mode, setMode] = useState<"create" | "rename" | null>(null);
  const [formError, setFormError] = useState("");
  const [notice, setNotice] = useState("");
  const [noteStatus, setNoteStatus] = useState("正在读取项目…");
  const [loadError, setLoadError] = useState(false);
  const [busy, setBusy] = useState(false);
  const [conflict, setConflict] = useState(false);
  const noteRef = useRef<ProjectNote | null>(null);
  const saving = useRef<Promise<boolean> | null>(null);
  noteRef.current = note;

  useEffect(() => {
    let cancel = false;
    setLoadError(false);
    client.list().then(async (next) => {
      const saved = await client.readNote();
      if (cancel) return;
      setRoster(next);
      setNote(saved);
      setDraft(saved.text);
      setConflict(false);
      setNoteStatus(saved.missing ? "还没有备注。" : "已保存在这个项目。");
    }).catch(() => {
      if (!cancel) setLoadError(true);
    });
    return () => { cancel = true; };
  }, [attempt, client]);

  useEffect(() => {
    if (!mode) return undefined;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) setMode(null);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [busy, mode]);

  if (loadError && !roster) {
    return (
      <main className="page project-desk">
        <h1>项目</h1>
        <p role="alert">没能读到项目。请再试一次。</p>
        <button type="button" className="btn" onClick={() => setAttempt((value) => value + 1)}>再试一次</button>
      </main>
    );
  }

  if (!roster || !note) return <main className="page project-desk"><p>正在读取项目…</p></main>;

  const current = activeName(roster.projects, roster.activeProjectId);
  const visible = roster.projects.filter((project) => !project.isArchived);

  async function keepNote(): Promise<boolean> {
    if (saving.current) return saving.current;
    const task = persistNote();
    saving.current = task;
    try {
      return await task;
    } finally {
      if (saving.current === task) saving.current = null;
    }
  }

  async function persistNote(): Promise<boolean> {
    const currentNote = noteRef.current;
    if (!currentNote) return false;
    if (conflict) {
      setNoteStatus(`备注还没保存，所以没有离开「${current}」。`);
      return false;
    }
    const outcome = await saveDraft(client, currentNote, draft);
    if (outcome.type === "saved") {
      noteRef.current = outcome.note;
      setNote(outcome.note);
      setNoteStatus("已保存在这个项目。");
      return true;
    }
    if (outcome.type === "clean") return true;
    if (outcome.type === "conflict") setConflict(true);
    setNoteStatus(`备注没保存，写下的内容还在。当前项目仍是「${current}」。`);
    return false;
  }

  function applyOpened(outcome: Extract<CreateOutcome, { type: "opened" }> | Extract<OpenOutcome, { type: "opened" }>) {
    setRoster(outcome.roster);
    setNote(outcome.note);
    setDraft(outcome.note.text);
    setConflict(false);
    setNotice(outcome.notice);
    setNoteStatus(noteLabel(outcome.noteUnread, outcome.note.missing));
    setMode(null);
    onChanged?.(outcome.roster);
  }

  return (
    <main className="page project-desk">
      <header className="page-head">
        <h1>项目</h1>
        <p>每个项目的备注都分开保存。新建或改名没成功时，你还留在现在这个项目。</p>
      </header>
      {notice ? <p className="desk-note" role="status">{notice}</p> : null}
      <section className="stack" aria-label="当前项目">
        <p className="hint">当前项目</p>
        <h2 className="current-name">{current}</h2>
        <div className="actions">
          <button type="button" className="btn-accent" disabled={busy} onClick={() => { setFormError(""); setMode("create"); }}>新建项目</button>
          <button type="button" className="btn" disabled={busy} onClick={() => { setFormError(""); setMode("rename"); }}>重命名</button>
        </div>
      </section>
      <section className="stack">
        <h2>项目备注</h2>
        <label htmlFor="project-note">只保存在「{current}」，不会出现在别的项目里。</label>
        <textarea
          id="project-note"
          value={draft}
          disabled={busy}
          onChange={(event) => { setDraft(event.target.value); setConflict(false); }}
          onBlur={() => { void keepNote(); }}
        />
        <p role="status">{noteStatus}</p>
        {conflict ? (
          <div className="actions">
            <button type="button" className="btn-accent" disabled={busy} onClick={() => void commitDraft()}>仍然保存输入框里的</button>
            <button type="button" className="btn" disabled={busy} onClick={() => void revertDraft()}>改回已保存的备注</button>
          </div>
        ) : null}
      </section>
      <section className="stack">
        <h2>这台电脑上的项目</h2>
        <ul className="catalog">
          {visible.map((project) => (
            <li key={project.id} className={project.id === roster.activeProjectId ? "is-current" : undefined}>
              <span className="catalog-main"><strong>{project.name}</strong></span>
              {project.id === roster.activeProjectId ? <span className="badge">当前</span> : (
                <button type="button" className="btn" disabled={busy} onClick={() => void go(project.id)}>打开「{project.name}」</button>
              )}
            </li>
          ))}
        </ul>
      </section>
      {mode ? (
        <NameDialog
          title={mode === "create" ? "新建项目" : "重命名当前项目"}
          body={mode === "create" ? "起一个新名字。它不会带走现在这个项目的备注。" : "只改现在这个项目的名字，里面的备注还在。"}
          confirmLabel={mode === "create" ? "创建并打开" : "重命名"}
          initial={mode === "rename" ? current : ""}
          projects={roster.projects}
          exceptId={mode === "rename" ? roster.activeProjectId : undefined}
          busy={busy}
          serverError={formError}
          onCancel={() => { if (!busy) setMode(null); }}
          onSubmit={(typed) => { void submit(typed); }}
        />
      ) : null}
    </main>
  );

  function showRoster(next: ProjectRoster, message: string) {
    setRoster(next);
    setNotice(message);
    setMode(null);
    onChanged?.(next);
  }

  function showRefusal(message: string, next?: ProjectRoster) {
    if (next) setRoster(next);
    setFormError(message);
  }

  async function submit(typed: string) {
    if (!(await keepNote())) {
      setFormError(`备注还没保存，所以没有继续。当前还是「${current}」。`);
      return;
    }
    setBusy(true);
    try {
      if (mode === "create") presentCreate(await createProject(client, roster!, typed));
      else presentRename(await renameProject(client, roster!, typed));
    } finally {
      setBusy(false);
    }
  }

  function presentCreate(outcome: CreateOutcome) {
    if (outcome.type === "opened") {
      applyOpened(outcome);
      return;
    }
    if (outcome.type === "kept") {
      showRoster(outcome.roster, outcome.notice);
      return;
    }
    showRefusal(outcome.message, outcome.type === "rejected" ? outcome.roster : undefined);
  }

  function presentRename(outcome: Awaited<ReturnType<typeof renameProject>>) {
    if (outcome.type === "renamed") {
      showRoster(outcome.roster, outcome.notice);
      return;
    }
    showRefusal(outcome.message, outcome.type === "rejected" ? outcome.roster : undefined);
  }

  async function go(id: string) {
    if (!(await keepNote())) {
      setNotice(`备注还没保存，所以没有离开「${current}」。`);
      return;
    }
    setBusy(true);
    try {
      presentOpen(await openProject(client, roster!, id));
    } finally {
      setBusy(false);
    }
  }

  function presentOpen(outcome: OpenOutcome) {
    if (outcome.type === "opened") {
      applyOpened(outcome);
      return;
    }
    if (outcome.type === "rejected") {
      if (outcome.roster) setRoster(outcome.roster);
      setNotice(outcome.message);
      return;
    }
    if (outcome.type === "blocked") setNotice(outcome.message);
  }

  async function commitDraft() {
    setBusy(true);
    try {
      const outcome = await saveDraftOver(client, draft);
      if (outcome.type === "saved") {
        setNote(outcome.note);
        setDraft(outcome.note.text);
        setConflict(false);
        setNoteStatus("已保存在这个项目。");
        return;
      }
      if (outcome.type === "conflict") setConflict(true);
      setNoteStatus(`备注没保存，写下的内容还在。当前项目仍是「${current}」。`);
    } finally {
      setBusy(false);
    }
  }

  async function revertDraft() {
    setBusy(true);
    try {
      const saved = await client.readNote();
      setNote(saved);
      setDraft(saved.text);
      setConflict(false);
      setNoteStatus(saved.missing ? "还没有备注。" : "已保存在这个项目。");
    } catch {
      setNoteStatus(`没能读回已保存的备注。输入框里的内容还在。当前项目仍是「${current}」。`);
    } finally {
      setBusy(false);
    }
  }
}

function noteLabel(unread: boolean, missing: boolean): string {
  if (unread) return "备注暂时没读到。";
  if (missing) return "还没有备注。";
  return "已保存在这个项目。";
}
