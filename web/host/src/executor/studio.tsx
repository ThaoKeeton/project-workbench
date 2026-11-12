import { useMemo, useState } from "react";
import "./studio.css";
import {
  actionLabels,
  authorMessages,
  authorPrompt,
  caseFromScript,
  isSceneAction,
  recordFromDom,
  repairPrompt,
  replay,
  sampleDom,
  sceneSnapshotText,
  seedCases,
  type ExecutorCase,
  type ExecutorRun,
  type ExecutorStep,
  type StepAction,
} from "./model";

const actions = Object.keys(actionLabels) as StepAction[];

export type ScriptCompleter = (prompt: string) => Promise<{ text: string; cost: number }>;

export function ExecutorStudio({ complete }: { complete?: ScriptCompleter }) {
  const [cases, setCases] = useState<ExecutorCase[]>(seedCases);
  const [selectedId, setSelectedId] = useState(seedCases()[0].id);
  const [history, setHistory] = useState<ExecutorRun[]>([]);
  const [view, setView] = useState<"edit" | "history">("edit");
  const [advanced, setAdvanced] = useState(false);
  const [goal, setGoal] = useState("");
  const [callsEnabled, setCallsEnabled] = useState(false);
  const [cap, setCap] = useState(0);
  const [spent, setSpent] = useState(0);
  const [modelCalls, setModelCalls] = useState(0);
  const [notice, setNotice] = useState<string>(authorMessages.replay);
  const [prompt, setPrompt] = useState("");
  const [draft, setDraft] = useState<ExecutorCase | null>(null);
  const [repairOn, setRepairOn] = useState(false);
  const [repairScript, setRepairScript] = useState<ExecutorCase | null>(null);
  const selected = cases.find((item) => item.id === selectedId) ?? cases[0];
  const latest = history.find((item) => item.caseId === selected.id);
  const structure = useMemo(() => `${sampleDom}\n${sceneSnapshotText}`, []);

  const updateStep = (id: string, field: keyof ExecutorStep, value: string) => {
    setCases(cases.map((item) => item.id === selected.id
      ? { ...item, script: undefined, steps: item.steps.map((entry) => entry.id === id ? { ...entry, [field]: value } : entry) }
      : item));
  };

  const record = () => {
    const recorded: ExecutorCase = { id: "CASE-REC", name: "录制 · 合成电子样品", steps: recordFromDom(sampleDom) };
    setCases([recorded, ...cases.filter((item) => item.id !== recorded.id)]);
    setSelectedId(recorded.id);
  };

  const run = () => {
    const result = replay(selected, sampleDom);
    setHistory([result, ...history.filter((item) => item.id !== result.id)]);
    setDraft(null);
    const failedResult = result.steps.find((item) => item.status === "failed");
    const failed = selected.steps.find((item) => item.id === failedResult?.id);
    if (repairOn && failed && failedResult) {
      setPrompt(repairPrompt(sampleDom, failed, failedResult.detail));
      setNotice(authorMessages.prepared);
      setRepairScript(null);
    } else {
      setPrompt("");
      setNotice(authorMessages.replay);
    }
  };

  const generate = async () => {
    const text = authorPrompt(goal, sampleDom, sceneSnapshotText);
    setPrompt(text);
    setDraft(null);
    if (goal.trim().length === 0) {
      setNotice(authorMessages.empty);
      return;
    }
    if (!callsEnabled) {
      setNotice(authorMessages.disabled);
      return;
    }
    if (spent >= cap) {
      setNotice(authorMessages.overBudget);
      return;
    }
    if (!complete) {
      setNotice(authorMessages.missingKey);
      return;
    }
    const result = await complete(text);
    setModelCalls((count) => count + 1);
    setSpent((value) => value + result.cost);
    const authored = caseFromScript(result.text);
    if (!authored) {
      setNotice(authorMessages.invalid);
      return;
    }
    setDraft(authored);
    setNotice(authorMessages.review);
  };

  const saveDraft = () => {
    if (!draft) return;
    setCases([draft, ...cases.filter((item) => item.id !== draft.id)]);
    setSelectedId(draft.id);
    setDraft(null);
    setNotice(authorMessages.saved);
  };

  const askRepair = async () => {
    if (!prompt) return;
    if (!callsEnabled) {
      setNotice(authorMessages.disabled);
      return;
    }
    if (spent >= cap) {
      setNotice(authorMessages.overBudget);
      return;
    }
    if (!complete) {
      setNotice(authorMessages.missingKey);
      return;
    }
    const result = await complete(prompt);
    setModelCalls((count) => count + 1);
    setSpent((value) => value + result.cost);
    const authored = caseFromScript(result.text);
    if (!authored) {
      setNotice(authorMessages.invalid);
      setRepairScript(null);
      return;
    }
    setRepairScript(authored);
    setNotice(authorMessages.review);
  };

  const applyRepair = () => {
    if (!repairScript) return;
    setCases(cases.map((item) => item.id === selected.id ? { ...repairScript, id: selected.id, name: selected.name } : item));
    setRepairScript(null);
    setNotice(authorMessages.saved);
  };

  return (
    <section className="executor" aria-label="自动化测试执行器">
      <header className="executor-head">
        <div>
          <h1>自动化测试执行器</h1>
          <p>用自然语言描述目标。系统只把页面文字和场景快照交给模型，审阅后才保存。截图留在本机，回放不调用模型。</p>
        </div>
        <div className="executor-actions">
          <button type="button" className="btn" onClick={run}>回放</button>
          <button type="button" className="btn" aria-pressed={view === "history"} onClick={() => setView(view === "history" ? "edit" : "history")}>
            {view === "history" ? "返回编辑" : "运行历史"}
          </button>
        </div>
      </header>
      {view === "history" ? <History runs={history} /> : (
        <div className="executor-main">
          <div className="author">
            <div className="author-controls">
              <label className="repair-toggle">
                <input type="checkbox" checked={callsEnabled} onChange={(event) => setCallsEnabled(event.target.checked)} />
                允许调用模型
              </label>
              <label>
                每日花费上限
                <input aria-label="每日花费上限" type="number" min={0} value={cap} onChange={(event) => setCap(Number(event.target.value))} />
              </label>
              <p>额度只留在本机预览目录，不会写入仓库。模型调用 {modelCalls} 次。</p>
            </div>
            <label>
              测试目标
              <textarea aria-label="测试目标" value={goal} placeholder="切削工件后体积减小，邻件不变" onChange={(event) => setGoal(event.target.value)} />
            </label>
            <div>
              <h2>当前页面文字结构</h2>
              <pre aria-label="当前页面文字结构">{structure}</pre>
            </div>
            <div className="executor-actions">
              <button type="button" className="btn-accent" onClick={() => void generate()}>生成脚本</button>
            </div>
            <p>{notice}</p>
            {prompt ? <pre aria-label="编写提示">{prompt}</pre> : null}
            {draft ? (
              <div className="review" aria-label="脚本审阅">
                <h2>{draft.name}</h2>
                <ol>
                  {draft.steps.map((entry) => (
                    <li key={entry.id}>{entry.id} · {actionLabels[entry.action]} · {entry.text || entry.selector} · {entry.expected}</li>
                  ))}
                </ol>
                <div className="executor-actions">
                  <button type="button" className="btn-accent" onClick={saveDraft}>确认保存</button>
                  <button type="button" className="btn" onClick={() => { setDraft(null); setNotice(authorMessages.replay); }}>放弃</button>
                </div>
              </div>
            ) : null}
            <ul className="case-list" aria-label="已保存用例">
              {cases.map((item) => (
                <li key={item.id}>
                  <button type="button" className={item.id === selected.id ? "is-on" : ""} onClick={() => setSelectedId(item.id)}>{item.name}</button>
                </li>
              ))}
            </ul>
            {latest ? <Replay result={latest} /> : <p>还没有这次回放。</p>}
            <button type="button" className="btn" aria-expanded={advanced} onClick={() => setAdvanced(!advanced)}>高级：手动步骤</button>
            {advanced ? (
              <ManualEditor selected={selected} onRecord={record} onChange={updateStep} />
            ) : null}
          </div>
          <aside className="repair" aria-label="AI 修复">
            <label className="repair-toggle">
              <input type="checkbox" checked={repairOn} onChange={(event) => { setRepairOn(event.target.checked); setPrompt(""); setRepairScript(null); }} />
              AI 修复
            </label>
            <p>{repairOn ? "打开后只准备文字提示。审阅后才应用。" : "修复入口默认关闭。打开后也只准备文本提示。"}</p>
            {repairOn && prompt ? <button type="button" className="btn" onClick={() => void askRepair()}>生成修复建议</button> : null}
            {repairScript ? (
              <div>
                <p>{repairScript.name}</p>
                <button type="button" className="btn-accent" onClick={applyRepair}>应用修复</button>
              </div>
            ) : null}
          </aside>
        </div>
      )}
    </section>
  );
}

function ManualEditor({ selected, onRecord, onChange }: { selected: ExecutorCase; onRecord: () => void; onChange: (id: string, field: keyof ExecutorStep, value: string) => void }) {
  return (
    <div className="case-editor">
      <div className="executor-actions">
        <h2>{selected.name}</h2>
        <button type="button" className="btn" onClick={onRecord}>从样品录制</button>
      </div>
      <ol>
        {selected.steps.map((entry) => (
          <li key={entry.id}>
            <div className="step-main">
              <label>
                动作
                <select aria-label={`${entry.id} 动作`} value={entry.action} onChange={(event) => onChange(entry.id, "action", event.target.value)}>
                  {actions.map((action) => <option key={action} value={action}>{actionLabels[action]}</option>)}
                </select>
              </label>
              <label>
                选择器
                <input aria-label={`${entry.id} 选择器`} value={entry.selector} onChange={(event) => onChange(entry.id, "selector", event.target.value)} />
              </label>
              <label>
                期望
                <input aria-label={`${entry.id} 期望`} value={entry.expected} onChange={(event) => onChange(entry.id, "expected", event.target.value)} />
              </label>
              <label>
                容差
                <input aria-label={`${entry.id} 容差`} value={entry.tolerance} onChange={(event) => onChange(entry.id, "tolerance", event.target.value)} />
              </label>
            </div>
            {isSceneAction(entry.action) ? (
              <label className="step-object">
                对象
                <input aria-label={`${entry.id} 对象`} value={entry.text} placeholder="名称、uuid 或路径" onChange={(event) => onChange(entry.id, "text", event.target.value)} />
              </label>
            ) : null}
          </li>
        ))}
      </ol>
    </div>
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
