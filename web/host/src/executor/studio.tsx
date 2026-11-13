import { useState } from "react";
import "./studio.css";
import {
  authorMessages,
  authorPrompt,
  caseFromScript,
  connectChoice,
  describeStep,
  fixedReplayScript,
  replay,
  resultSentence,
  sampleAppName,
  sampleDom,
  sceneSnapshotText,
  type ExecutorCase,
  type ExecutorRun,
} from "./model";

export type ScriptCompleter = (prompt: string) => Promise<{ text: string; cost: number }>;

const steps = ["选择程序", "说一句", "看一遍", "每天重跑"] as const;

export function ExecutorStudio({ complete }: { complete?: ScriptCompleter }) {
  const [phase, setPhase] = useState<"empty" | "wizard" | "result">("empty");
  const [stepIndex, setStepIndex] = useState(0);
  const [appName, setAppName] = useState("");
  const [connected, setConnected] = useState(false);
  const [status, setStatus] = useState("还没有选择");
  const [hint, setHint] = useState("请选择一个程序，或从最近用过的里面点一个。");
  const [recent, setRecent] = useState<string[]>([sampleAppName]);
  const [goal, setGoal] = useState("");
  const [cap, setCap] = useState(20);
  const [spent, setSpent] = useState(0);
  const [draft, setDraft] = useState<ExecutorCase | null>(null);
  const [notice, setNotice] = useState("");
  const [demo, setDemo] = useState<ExecutorRun | null>(null);
  const [saved, setSaved] = useState<ExecutorCase | null>(null);
  const [dailyAt, setDailyAt] = useState("09:00");
  const [scheduled, setScheduled] = useState(false);
  const keyReady = Boolean(complete) || readFakeModel();

  const choose = (name: string) => {
    const result = connectChoice(name);
    setAppName(name);
    setConnected(result.ok);
    setStatus(result.status);
    setHint(result.hint);
    if (result.ok) setRecent([name, ...recent.filter((item) => item !== name)].slice(0, 4));
  };

  const draftCase = async () => {
    setDraft(null);
    setDemo(null);
    if (goal.trim().length === 0) {
      setNotice(authorMessages.empty);
      return;
    }
    if (!keyReady) return;
    if (spent >= cap) {
      setNotice(authorMessages.overBudget);
      return;
    }
    const prompt = authorPrompt(goal, sampleDom, sceneSnapshotText);
    const result = complete
      ? await complete(prompt)
      : { text: fixedReplayScript, cost: 0 };
    setSpent((value) => value + result.cost);
    const authored = caseFromScript(result.text);
    if (!authored) {
      setNotice(authorMessages.invalid);
      return;
    }
    setDraft(authored);
    setNotice(authorMessages.review);
  };

  const showDemo = () => {
    if (!draft) return;
    const result = replay(draft, sampleDom);
    setDemo(result);
    setPhase("result");
  };

  const saveDraft = () => {
    if (!draft || !demo) return;
    setSaved(draft);
    setNotice(authorMessages.saved);
    setStepIndex(3);
    setPhase("wizard");
  };

  return (
    <section className="executor" aria-label="自动化测试执行器">
      <header className="executor-head">
        <div>
          <h1>自动化测试执行器</h1>
          <p>选一个程序，用一句话说要测什么。看它做一遍，再决定要不要留下。</p>
        </div>
      </header>
      {phase === "empty" ? (
        <div className="wizard-card" aria-label="开始">
          <h2>还没有开始</h2>
          <p>先选择要测的程序。连上之后，用一句话说明要检查什么，再看着它做一遍。</p>
          <button type="button" className="btn-accent" onClick={() => setPhase("wizard")}>选择程序</button>
        </div>
      ) : null}
      {phase === "wizard" ? (
        <div className="wizard">
          <ol className="wizard-steps" aria-label="步骤">
            {steps.map((label, index) => (
              <li key={label}>
                <button type="button" aria-current={index === stepIndex ? "step" : undefined} onClick={() => setStepIndex(index)}>{index + 1}. {label}</button>
              </li>
            ))}
          </ol>
          {stepIndex === 0 ? (
            <AppStep appName={appName} connected={connected} status={status} hint={hint} recent={recent} onChoose={choose} onNext={() => setStepIndex(1)} />
          ) : null}
          {stepIndex === 1 ? (
            <GoalStep goal={goal} onGoal={setGoal} onBack={() => setStepIndex(0)} onNext={() => { setStepIndex(2); void draftCase(); }} />
          ) : null}
          {stepIndex === 2 ? (
            <DraftStep
              goal={goal}
              cap={cap}
              spent={spent}
              keyReady={keyReady}
              draft={draft}
              notice={notice}
              onCap={setCap}
              onRetry={() => void draftCase()}
              onDemo={showDemo}
              onBack={() => setStepIndex(1)}
            />
          ) : null}
          {stepIndex === 3 ? (
            <ScheduleStep
              saved={saved}
              dailyAt={dailyAt}
              scheduled={scheduled}
              onTime={setDailyAt}
              onSave={() => setScheduled(true)}
              onBack={() => setStepIndex(2)}
            />
          ) : null}
        </div>
      ) : null}
      {phase === "result" && demo && draft ? (
        <ResultPage run={demo} canSave={Boolean(draft)} onSave={saveDraft} onBack={() => setPhase("wizard")} />
      ) : null}
    </section>
  );
}

function readFakeModel(): boolean {
  try {
    return window.sessionStorage.getItem("workbench.author.fake") === "1";
  } catch {
    return false;
  }
}

function AppStep({
  appName,
  connected,
  status,
  hint,
  recent,
  onChoose,
  onNext,
}: {
  appName: string;
  connected: boolean;
  status: string;
  hint: string;
  recent: string[];
  onChoose: (name: string) => void;
  onNext: () => void;
}) {
  return (
    <div className="wizard-card" aria-label="选择程序">
      <h2>选择要测的程序</h2>
      <p>选好后会由这里打开并连上。打不开时，换一个可以自己运行的程序再试。</p>
      <div className="executor-actions">
        <label className="btn">
          浏览
          <input
            aria-label="浏览程序"
            type="file"
            accept=".exe,.app,application/octet-stream"
            onChange={(event) => {
              const file = event.target.files?.[0];
              if (file) onChoose(file.name);
            }}
          />
        </label>
      </div>
      <p>最近用过的</p>
      <ul className="case-list" aria-label="最近用过的程序">
        {recent.map((name) => (
          <li key={name}><button type="button" className={name === appName ? "is-on" : ""} onClick={() => onChoose(name)}>{name}</button></li>
        ))}
      </ul>
      <p role="status">{status}。{hint}</p>
      {!connected && appName ? <button type="button" className="btn" onClick={() => onChoose("")}>重新选择</button> : null}
      <button type="button" className="btn-accent" disabled={!connected} onClick={onNext}>下一步</button>
    </div>
  );
}

function GoalStep({ goal, onGoal, onBack, onNext }: { goal: string; onGoal: (value: string) => void; onBack: () => void; onNext: () => void }) {
  return (
    <div className="wizard-card" aria-label="说一句">
      <h2>用一句话说明要测什么</h2>
      <p>打开之后会自动看一遍画面和文字，不用你再操作。</p>
      <label>
        要测什么
        <textarea aria-label="要测什么" value={goal} placeholder="切削工件后，工件变小，旁边的牙不变" onChange={(event) => onGoal(event.target.value)} />
      </label>
      <div className="executor-actions">
        <button type="button" className="btn" onClick={onBack}>上一步</button>
        <button type="button" className="btn-accent" disabled={goal.trim().length === 0} onClick={onNext}>下一步</button>
      </div>
    </div>
  );
}

function DraftStep({
  goal,
  cap,
  spent,
  keyReady,
  draft,
  notice,
  onCap,
  onRetry,
  onDemo,
  onBack,
}: {
  goal: string;
  cap: number;
  spent: number;
  keyReady: boolean;
  draft: ExecutorCase | null;
  notice: string;
  onCap: (value: number) => void;
  onRetry: () => void;
  onDemo: () => void;
  onBack: () => void;
}) {
  return (
    <div className="wizard-card" aria-label="看一遍">
      <h2>看它准备做什么</h2>
      {!keyReady ? (
        <p role="status">
          {authorMessages.missingKey}
          <a href="/settings">去设置</a>
        </p>
      ) : null}
      <label>
        每天最多问几次
        <input aria-label="每天最多问几次" type="number" min={0} value={cap} onChange={(event) => onCap(Number(event.target.value))} />
      </label>
      <p>到这个次数就不再问。已经保存的检查照常重跑。这里不写金额。今天已问 {spent} 次。</p>
      {notice ? <p role="status">{notice}</p> : null}
      {keyReady ? <button type="button" className="btn" onClick={onRetry}>{goal.trim() ? "再问一次" : "先写一句"}</button> : null}
      {draft ? (
        <div aria-label="检查清单">
          <h3>{draft.name}</h3>
          <ol className="checklist">
            {draft.steps.map((item) => {
              const line = describeStep(item);
              return <li key={item.id}><span>{line.will}</span><span>{line.check}</span></li>;
            })}
          </ol>
          <details>
            <summary>详情</summary>
            <pre>{draft.script ?? draft.steps.map((item) => `${item.id} ${item.action} ${item.expected}`).join("\n")}</pre>
          </details>
          <div className="executor-actions">
            <button type="button" className="btn-accent" onClick={onDemo}>演示一遍</button>
          </div>
        </div>
      ) : null}
      <button type="button" className="btn" onClick={onBack}>上一步</button>
    </div>
  );
}

function ScheduleStep({
  saved,
  dailyAt,
  scheduled,
  onTime,
  onSave,
  onBack,
}: {
  saved: ExecutorCase | null;
  dailyAt: string;
  scheduled: boolean;
  onTime: (value: string) => void;
  onSave: () => void;
  onBack: () => void;
}) {
  return (
    <div className="wizard-card" aria-label="每天重跑">
      <h2>每天自动再跑一遍</h2>
      {saved ? <p>已留下「{saved.name}」。到点只重跑这一份，不会再问。</p> : <p>先看完演示并保存，再安排每天的时间。</p>}
      <label>
        每天重跑的时间
        <input aria-label="每天重跑的时间" type="time" value={dailyAt} onChange={(event) => onTime(event.target.value)} />
      </label>
      <button type="button" className="btn-accent" disabled={!saved} onClick={onSave}>保存安排</button>
      {scheduled ? <p role="status">已记下。到 {dailyAt} 会再跑一遍，不会再问。</p> : null}
      <button type="button" className="btn" onClick={onBack}>上一步</button>
    </div>
  );
}

function ResultPage({ run, canSave, onSave, onBack }: { run: ExecutorRun; canSave: boolean; onSave: () => void; onBack: () => void }) {
  return (
    <div className="wizard-card result-page" aria-label="结果">
      <p className={run.passed ? "result-mark pass" : "result-mark fail"}>{run.passed ? "通过" : "没通过"}</p>
      <p role="status">{resultSentence(run.passed)}</p>
      <div className="frames" aria-label="前后画面">
        <figure>
          <svg viewBox="0 0 120 80" role="img" aria-label="之前"><rect width="120" height="80" /><rect x="36" y="20" width="48" height="40" /></svg>
          <figcaption>之前。只留在这台电脑上。</figcaption>
        </figure>
        <figure>
          <svg viewBox="0 0 120 80" role="img" aria-label="之后"><rect width="120" height="80" /><rect x="46" y="30" width="28" height="24" /></svg>
          <figcaption>之后。只留在这台电脑上。</figcaption>
        </figure>
      </div>
      <details>
        <summary>详情</summary>
        <pre>{run.steps.map((item) => `${item.id} ${item.status} ${item.detail}`).join("\n")}</pre>
      </details>
      <div className="executor-actions">
        {canSave ? <button type="button" className="btn-accent" onClick={onSave}>保存</button> : null}
        <button type="button" className="btn" onClick={onBack}>{run.passed ? "返回" : "返回修改说明"}</button>
      </div>
    </div>
  );
}
