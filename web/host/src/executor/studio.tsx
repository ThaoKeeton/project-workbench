import { useMemo, useState } from "react";
import "./studio.css";
import { recordFromDom, repairPrompt, replay, sampleDom, seedCases, type ExecutorCase, type ExecutorRun, type ExecutorStep, type StepAction } from "./model";

const actions: StepAction[] = ["attach", "click", "click-scene", "type", "wait-for", "assert-text", "assert-attr", "assert-number", "screenshot"];

export function ExecutorStudio() {
  const [cases, setCases] = useState<ExecutorCase[]>(seedCases);
  const [selectedId, setSelectedId] = useState(seedCases()[0].id);
  const [history, setHistory] = useState<ExecutorRun[]>([]);
  const [view, setView] = useState<"edit" | "history">("edit");
  const [ai, setAi] = useState(false);
  const [cap, setCap] = useState(0);
  const [prompt, setPrompt] = useState("");
  const selected = cases.find((item) => item.id === selectedId) ?? cases[0];
  const latest = history.find((item) => item.caseId === selected.id);

  const spend = useMemo(() => (ai && cap > 0 ? "已准备文本提示，模型调用 0 次" : "正常回放不调用模型"), [ai, cap]);

  const updateStep = (id: string, field: keyof ExecutorStep, value: string) => {
    setCases(cases.map((item) => item.id === selected.id
      ? { ...item, steps: item.steps.map((entry) => entry.id === id ? { ...entry, [field]: value } : entry) }
      : item));
  };

  const record = () => {
    const recorded: ExecutorCase = { id: "CASE-REC", name: "录制 · 合成电子样品", steps: recordFromDom(sampleDom) };
    setCases([recorded, ...cases.filter((item) => item.id !== recorded.id)]);
    setSelectedId(recorded.id);
    setPrompt("");
  };

  const run = () => {
    const result = replay(selected, sampleDom);
    setHistory([result, ...history.filter((item) => item.id !== result.id)]);
    const failed = selected.steps.find((item) => result.steps.some((step) => step.id === item.id && step.status === "failed"));
    setPrompt(ai && cap > 0 && failed ? repairPrompt(sampleDom, failed) : "");
  };

  return (
    <section className="executor" aria-label="自动化测试执行器">
      <header className="executor-head">
        <div>
          <h1>自动化测试执行器</h1>
          <p>目标是合成电子样品。录制只保留文本 DOM，回放不发送截图，自动发送保持关闭。</p>
        </div>
        <div className="executor-actions">
          <button type="button" className="btn" onClick={record}>从样品录制</button>
          <button type="button" className="btn-accent" onClick={run}>回放</button>
          <button type="button" className="btn" aria-pressed={view === "history"} onClick={() => setView(view === "history" ? "edit" : "history")}>
            {view === "history" ? "返回编辑" : "运行历史"}
          </button>
        </div>
      </header>
      {view === "history" ? <History runs={history} /> : (
        <div className="executor-body">
          <ul className="case-list" aria-label="用例列表">
            {cases.map((item) => (
              <li key={item.id}>
                <button type="button" className={item.id === selected.id ? "is-on" : ""} onClick={() => setSelectedId(item.id)}>{item.name}</button>
              </li>
            ))}
          </ul>
          <div className="case-editor">
            <h2>{selected.name}</h2>
            <ol>
              {selected.steps.map((entry) => (
                <li key={entry.id}>
                  <label>
                    动作
                    <select aria-label={`${entry.id} 动作`} value={entry.action} onChange={(event) => updateStep(entry.id, "action", event.target.value)}>
                      {actions.map((action) => <option key={action} value={action}>{action}</option>)}
                    </select>
                  </label>
                  <label>
                    选择器
                    <input aria-label={`${entry.id} 选择器`} value={entry.selector} onChange={(event) => updateStep(entry.id, "selector", event.target.value)} />
                  </label>
                  <label>
                    期望
                    <input aria-label={`${entry.id} 期望`} value={entry.expected} onChange={(event) => updateStep(entry.id, "expected", event.target.value)} />
                  </label>
                  <label>
                    容差
                    <input aria-label={`${entry.id} 容差`} value={entry.tolerance} onChange={(event) => updateStep(entry.id, "tolerance", event.target.value)} />
                  </label>
                </li>
              ))}
            </ol>
            {latest ? <Replay result={latest} /> : <p>还没有这次回放。</p>}
          </div>
        </div>
      )}
      <aside className="repair" aria-label="AI 修复">
        <label className="repair-toggle">
          <input type="checkbox" checked={ai} onChange={(event) => { setAi(event.target.checked); setPrompt(""); }} />
          AI 修复
        </label>
        <label>
          每日额度
          <input aria-label="每日额度" type="number" min={0} value={cap} onChange={(event) => setCap(Number(event.target.value))} />
        </label>
        <p>{spend}</p>
        {prompt ? <pre>{prompt}</pre> : <p>修复入口默认关闭。打开后也只准备文本提示。</p>}
      </aside>
    </section>
  );
}

function Replay({ result }: { result: ExecutorRun }) {
  return (
    <div>
      <p role="status">{result.passed ? "全部通过" : "有步骤失败"}</p>
      <ul className="replay-steps">
        {result.steps.map((item) => (
          <li key={item.id}>
            <span>{item.id}</span>
            <span>{item.status === "passed" ? "通过" : "失败"}</span>
            <span>{item.ms} ms</span>
            <span>{item.evidence || item.detail}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function History({ runs }: { runs: ExecutorRun[] }) {
  if (runs.length === 0) return <p>还没有运行历史。</p>;
  return (
    <ul className="run-history" aria-label="运行历史">
      {runs.map((item) => (
        <li key={item.id}>{item.caseId} · {item.passed ? "通过" : "失败"} · {item.steps.length} 步</li>
      ))}
    </ul>
  );
}
