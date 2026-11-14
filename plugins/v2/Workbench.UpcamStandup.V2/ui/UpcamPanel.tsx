import { useEffect, useState } from "react";
import "../../shared/panel.css";

export type DigestItem = { id: string; title: string; owner: string; status: string };
export type NoteFile = { name: string; text: string };
export type Digest = {
  schema: string;
  schemaVersion: number;
  autoSend: boolean;
  delivered: boolean;
  stored: boolean;
  source: string;
  meetingDate: string;
  summary: string;
  speechSummary: string;
  items: DigestItem[];
  speeches: { owner: string; text: string }[];
  skipped: { name: string; reason: string }[];
  issues: string[];
};

const schema = "workbench.upcam.digest.v2.preview";
const skipLabel: Record<string, string> = { NotToday: "不是今日记录", UnreadableDate: "日期无法识别" };

type Props = {
  load: () => Promise<Digest>;
  importNotes: (files: NoteFile[]) => Promise<Digest>;
  loadSample: () => Promise<Digest>;
};

export async function readLocalNotes(list: FileList | File[]): Promise<NoteFile[]> {
  const accepted: NoteFile[] = [];
  for (const file of [...list]) {
    if (file.size > 65536) throw new Error("FileTooLarge");
    if (!/\.(txt|md|json)$/i.test(file.name)) continue;
    accepted.push({ name: file.name, text: await file.text() });
  }
  if (accepted.length === 0) throw new Error("NoText");
  if (accepted.length > 32) throw new Error("TooMany");
  return accepted;
}

export function UpcamPanel(props: Props) {
  const [digest, setDigest] = useState<Digest | null>(null);
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    props.load().then(
      (value) => {
        if (cancelled) return;
        if (isDigest(value)) setDigest(value);
        else setError("无法读取站会草稿。");
      },
      () => {
        if (!cancelled) setError("无法读取站会草稿。");
      },
    );
    return () => {
      cancelled = true;
    };
  }, [props.load]);

  const apply = (action: () => Promise<Digest>, message: string) => {
    setBusy(true);
    setError("");
    action().then(
      (value) => {
        if (!isDigest(value) || value.autoSend || value.delivered) {
          setError("没有写入草稿。");
          return;
        }
        setDigest(value);
        setStatus(message);
      },
      () => setError("没有写入草稿。"),
    ).finally(() => setBusy(false));
  };

  const onFiles = (list: FileList | null) => {
    if (!list || list.length === 0) return;
    readLocalNotes(list).then(
      (files) => apply(() => props.importNotes(files), "已从本机导入，未发送。"),
      (reason: unknown) => setError(reason instanceof Error && reason.message === "FileTooLarge" ? "文件太大。" : reason instanceof Error && reason.message === "TooMany" ? "文件太多。" : "没有导入。"),
    );
  };

  return (
    <article className="doc digest">
      <header className="doc-head">
        <div>
          <h1>站会日报</h1>
          <p>{digest?.meetingDate ?? "今日"} · 自动发送已关闭 · 草稿只留在本机</p>
        </div>
        <div className="row-actions">
          <button type="button" className="btn" disabled={busy} onClick={() => apply(props.loadSample, "已载入合成样本，未发送。")}>载入合成样本</button>
          <label className="btn file-btn">
            选择站会文件
            <input type="file" accept=".txt,.md,.json,text/plain" disabled={busy} onChange={(event) => onFiles(event.target.files)} />
          </label>
          <label className="btn file-btn">
            选择站会文件夹
            <input type="file" accept=".txt,.md,.json,text/plain" multiple disabled={busy} webkitdirectory="" onChange={(event) => onFiles(event.target.files)} />
          </label>
        </div>
      </header>
      {error ? <p role="alert">{error}</p> : null}
      {!digest && !error ? <p>正在读取站会草稿…</p> : null}
      {digest ? (
        <>
          <h2>摘要</h2>
          <p>{digest.summary}</p>
          <h2>今日事项</h2>
          {digest.items.length === 0 ? <p>今日还没有事项。</p> : (
            <table className="plugin-table">
              <caption className="sr">今日事项</caption>
              <thead>
                <tr><th>编号</th><th>标题</th><th>负责人</th><th>状态</th></tr>
              </thead>
              <tbody>
                {digest.items.map((item) => (
                  <tr key={item.id}>
                    <td>{item.id}</td>
                    <td>{item.title}</td>
                    <td>{item.owner}</td>
                    <td>{item.status}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <h2>发言摘录</h2>
          {digest.speeches.length === 0 ? <p>{digest.speechSummary || "今日事项没有发言摘录。"}</p> : digest.speeches.map((speech) => (
            <p className="speech" key={`${speech.owner}-${speech.text}`}><strong>{speech.owner}</strong><br />{speech.text}</p>
          ))}
          {digest.skipped.length > 0 ? (
            <ul className="plain">
              {digest.skipped.map((item) => <li key={item.name}>{item.name}：{skipLabel[item.reason] ?? item.reason}</li>)}
            </ul>
          ) : null}
          {digest.issues.length > 0 ? <ul className="plain">{digest.issues.map((issue) => <li key={issue}>{issue}</li>)}</ul> : null}
        </>
      ) : null}
      {status ? <p role="status">{status}</p> : null}
    </article>
  );
}

function isDigest(value: unknown): value is Digest {
  if (!value || typeof value !== "object") return false;
  const document = value as Digest;
  return document.schema === schema && document.schemaVersion === 2 && Array.isArray(document.items) && Array.isArray(document.speeches);
}
