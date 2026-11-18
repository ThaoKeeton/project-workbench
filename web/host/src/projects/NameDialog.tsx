import { useEffect, useState } from "react";
import { nameFeedback, type NamedProject } from "./names";

export function NameDialog({
  title,
  body,
  confirmLabel,
  initial,
  projects,
  exceptId,
  busy,
  serverError,
  onCancel,
  onSubmit,
}: {
  title: string;
  body: string;
  confirmLabel: string;
  initial: string;
  projects: readonly NamedProject[];
  exceptId?: string;
  busy: boolean;
  serverError: string;
  onCancel: () => void;
  onSubmit: (name: string) => void;
}) {
  const [typed, setTyped] = useState(initial);
  const [touched, setTouched] = useState(false);
  const [heldError, setHeldError] = useState(serverError);
  useEffect(() => setHeldError(serverError), [serverError]);
  const live = typed.trim().length > 0 ? nameFeedback(typed, projects, exceptId) : "";
  const empty = touched && typed.trim().length === 0 ? nameFeedback(typed, projects, exceptId) : "";
  const message = live || empty || heldError;

  return (
    <div className="scrim" role="presentation">
      <form
        className="dialog project-name-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="project-name-title"
        onSubmit={(event) => {
          event.preventDefault();
          const problem = nameFeedback(typed, projects, exceptId);
          if (problem) {
            setTouched(true);
            return;
          }
          onSubmit(typed);
        }}
      >
        <h2 id="project-name-title">{title}</h2>
        <p className="hint">{body}</p>
        <label htmlFor="project-name">项目名称</label>
        <input
          id="project-name"
          type="text"
          value={typed}
          autoFocus
          autoComplete="off"
          disabled={busy}
          aria-invalid={message ? true : undefined}
          aria-describedby={message ? "project-name-error" : undefined}
          onChange={(event) => {
            setTyped(event.target.value);
            setTouched(false);
            setHeldError("");
          }}
        />
        {message ? <p id="project-name-error" className="form-error" role="alert">{message}</p> : null}
        <div className="row-actions">
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>取消</button>
          <button type="submit" className="btn-accent" disabled={busy}>{confirmLabel}</button>
        </div>
      </form>
    </div>
  );
}
