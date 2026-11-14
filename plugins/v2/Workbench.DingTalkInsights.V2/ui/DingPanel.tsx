import { useEffect, useState } from "react";
import "../../shared/panel.css";

export type Evidence = { messageId: string; excerpt: string };
export type Insight = { id: string; kind: string; title: string; detail: string; evidence: Evidence[] };
export type InsightState = {
  schema: string;
  schemaVersion: number;
  autoSend: boolean;
  sendEnabled: boolean;
  webhookConfigured: boolean;
  delivered: boolean;
  stored: boolean;
  source: string;
  imported: boolean;
  analyzed: boolean;
  conversation: string;
  messageCount: number;
  insights: Insight[];
};

const schema = "workbench.dingtalk.insights.v2.preview";

type Props = {
  load: () => Promise<InsightState>;
  importChat: (text: string) => Promise<InsightState>;
  loadSample: () => Promise<InsightState>;
  analyze: () => Promise<InsightState>;
};

export async function readChatFile(file: File): Promise<string> {
  if (file.size > 65536) throw new Error("FileTooLarge");
  if (!/\.(txt|md|json)$/i.test(file.name)) throw new Error("NoText");
  return file.text();
}

export function DingPanel(props: Props) {
  const [state, setState] = useState<InsightState | null>(null);
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    props.load().then(
      (value) => {
        if (cancelled) return;
        if (isState(value)) setState(value);
        else setError("无法读取洞察。");
      },
      () => {
        if (!cancelled) setError("无法读取洞察。");
      },
    );
    return () => {
      cancelled = true;
    };
  }, [props.load]);

  const apply = (action: () => Promise<InsightState>, message: string, failure = "没有完成本地分析。") => {
    setBusy(true);
    setError("");
    action().then(
      (value) => {
        if (!isState(value)) {
          setError(failure);
          return;
        }
        setState(value);
        setStatus(message);
      },
      () => setError(failure),
    ).finally(() => setBusy(false));
  };

  return (
    <article className="doc insights">
      <header className="doc-head">
        <div>
          <h1>钉钉 Insights</h1>
          <p>只分析已导入的聊天导出。自动发送已关闭，不会发给钉钉，也没有 webhook。</p>
        </div>
        <div className="row-actions">
          <button type="button" className="btn" disabled={busy} onClick={() => apply(props.loadSample, "已载入合成导出，尚未分析。", "没有导入。")}>载入合成样本</button>
          <label className="btn file-btn">
            选择聊天导出
            <input
              type="file"
              accept=".txt,.md,.json,text/plain"
              disabled={busy}
              onChange={(event) => {
                const file = event.target.files?.[0];
                if (!file) return;
                readChatFile(file).then(
                  (text) => apply(() => props.importChat(text), "已导入本机导出，尚未分析。", "没有导入。"),
                  (reason: unknown) => setError(reason instanceof Error && reason.message === "FileTooLarge" ? "文件太大。" : "没有导入。"),
                );
              }}
            />
          </label>
          <button type="button" className="btn-accent" disabled={busy || !state?.imported} onClick={() => apply(props.analyze, "已按需生成洞察，未发送。")}>按需分析</button>
          <button type="button" className="btn" disabled>发送已关闭</button>
        </div>
      </header>
      {error ? <p role="alert">{error}</p> : null}
      {!state && !error ? <p>正在读取洞察…</p> : null}
      {state ? (
        <>
          <p>{state.imported ? `${state.conversation} · ${state.messageCount} 条消息` : "还没有导入聊天导出。"}</p>
          <p>{state.analyzed ? "以下洞察只引用导出原文。" : "导入之后才会按需分析，不会自动开始。"}</p>
          {state.insights.length === 0 ? <p>还没有洞察。</p> : (
            <ul className="plain">
              {state.insights.map((insight) => (
                <li key={insight.id}>
                  <strong>{insight.title}</strong>
                  <div>{insight.detail}</div>
                  {insight.evidence.map((item) => (
                    <p className="evidence" key={item.messageId}><span>{item.messageId}</span><q>{item.excerpt}</q></p>
                  ))}
                </li>
              ))}
            </ul>
          )}
        </>
      ) : null}
      {status ? <p role="status">{status}</p> : null}
    </article>
  );
}

function isState(value: unknown): value is InsightState {
  if (!value || typeof value !== "object") return false;
  const document = value as InsightState;
  return document.schema === schema && document.schemaVersion === 2 && document.autoSend === false
    && document.sendEnabled === false && document.webhookConfigured === false && document.delivered === false
    && Array.isArray(document.insights);
}
