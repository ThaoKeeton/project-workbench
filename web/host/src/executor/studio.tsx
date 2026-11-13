import { useEffect, useState } from "react";
import "./studio.css";
import {
  authorMessages,
  authorPrompt,
  caseFromScript,
  connectChoice,
  failingReplayScript,
  fixedReplayScript,
  humanFindings,
  plainPlan,
  readDailyCap,
  replay,
  resultSentence,
  sampleAppName,
  sampleDom,
  sceneSnapshotText,
  type ExecutorCase,
  type ExecutorRun,
  type Finding,
} from "./model";

export type ScriptCompleter = (prompt: string) => Promise<{ text: string; cost: number }>;

const stepNames = ["选择程序", "说一句", "看一遍", "每天重跑"] as const;

type SavedTest = { id: string; name: string; passed: boolean; at: string; dailyAt: string };

export function ExecutorStudio({ complete, stepDelay = 160 }: { complete?: ScriptCompleter; stepDelay?: number }) {
  const [phase, setPhase] = useState<"home" | "wizard" | "demo" | "result">("home");
  const [stepIndex, setStepIndex] = useState(0);
  const [appName, setAppName] = useState("");
  const [connected, setConnected] = useState(false);
  const [status, setStatus] = useState("还没有选择");
  const [hint, setHint] = useState("请选择一个程序，或从最近用过的里面点一个。");
  const [recent, setRecent] = useState<string[]>([sampleAppName]);
  const [goal, setGoal] = useState("");
  const [correction, setCorrection] = useState("");
  const [cap] = useState(readDailyCap);
  const [spent, setSpent] = useState(0);
  const [draft, setDraft] = useState<ExecutorCase | null>(null);
  const [notice, setNotice] = useState("");
  const [demo, setDemo] = useState<ExecutorRun | null>(null);
  const [findings, setFindings] = useState<Finding[]>([]);
  const [shown, setShown] = useState(0);
  const [stopped, setStopped] = useState(false);
  const [library, setLibrary] = useState<SavedTest[]>([]);
  const [dailyAt, setDailyAt] = useState("09:00");
  const [scheduled, setScheduled] = useState(false);
  const keyReady = Boolean(complete) || readFake() !== "off";
  const pace = readPace(stepDelay);
  const left = Math.max(0, cap - spent);

  useEffect(() => {
    if (phase !== "demo" || stopped || !demo) return;
    if (shown >= findings.length) {
      const timer = window.setTimeout(() => setPhase("result"), pace);
      return () => window.clearTimeout(timer);
    }
    const timer = window.setTimeout(() => setShown((count) => count + 1), pace);
    return () => window.clearTimeout(timer);
  }, [phase, shown, stopped, pace, findings.length, demo]);

  const choose = (name: string) => {
    const result = connectChoice(name);
    setAppName(name);
    setConnected(result.ok);
    setStatus(result.status);
    setHint(result.hint);
    if (result.ok) setRecent([name, ...recent.filter((item) => item !== name)].slice(0, 4));
  };

  const draftCase = async (note = correction) => {
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
    const prompt = authorPrompt([goal, note].filter((part) => part.trim()).join("\n"), sampleDom, sceneSnapshotText);
    const result = complete ? await complete(prompt) : { text: readFake() === "fail" ? failingReplayScript : fixedReplayScript, cost: 0 };
    setSpent((value) => value + result.cost);
    const authored = caseFromScript(result.text);
    if (!authored) {
      setNotice(authorMessages.invalid);
      return;
    }
    setDraft(authored);
    setNotice("");
  };

  const showDemo = () => {
    if (!draft) return;
    const result = replay(draft, sampleDom);
    setDemo(result);
    setFindings(humanFindings(draft, result));
    setShown(0);
    setStopped(false);
    setPhase("demo");
  };

  const saveAndSchedule = () => {
    if (!draft || !demo) return;
    setLibrary([{
      id: draft.id,
      name: draft.name,
      passed: demo.passed,
      at: new Date().toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit", hour12: false }),
      dailyAt,
    }, ...library.filter((item) => item.id !== draft.id)]);
    setScheduled(false);
    setStepIndex(3);
    setPhase("wizard");
  };

  return (
    <section className="executor" aria-label="自动测试">
      <header className="executor-head">
        <div>
          <h1>自动测试</h1>
          <p>选一个程序，用一句话说要测什么。看它做一遍，再决定要不要留下。</p>
        </div>
      </header>
      {phase === "home" ? (
        <Home library={library} onNew={() => { setStepIndex(0); setPhase("wizard"); }} />
      ) : null}
      {phase === "wizard" ? (
        <div className="wizard">
          <ol className="wizard-steps" aria-label="步骤">
            {stepNames.map((label, index) => (
              <li key={label} className={index < stepIndex ? "is-done" : index === stepIndex ? "is-current" : "is-later"}>
                {index < stepIndex ? (
                  <button type="button" onClick={() => setStepIndex(index)}>{label}</button>
                ) : (
                  <span aria-current={index === stepIndex ? "step" : undefined}>{label}</span>
                )}
              </li>
            ))}
          </ol>
          {stepIndex === 0 ? <AppStep appName={appName} connected={connected} status={status} hint={hint} recent={recent} onChoose={choose} onNext={() => setStepIndex(1)} /> : null}
          {stepIndex === 1 ? <GoalStep goal={goal} onGoal={setGoal} onBack={() => setStepIndex(0)} onNext={() => { setStepIndex(2); void draftCase(""); }} /> : null}
          {stepIndex === 2 ? (
            <DraftStep
              left={left}
              keyReady={keyReady}
              draft={draft}
              notice={notice}
              correction={correction}
              onCorrection={setCorrection}
              onRetry={() => void draftCase(correction)}
              onDemo={showDemo}
              onBack={() => setStepIndex(1)}
            />
          ) : null}
          {stepIndex === 3 ? (
            <ScheduleStep dailyAt={dailyAt} scheduled={scheduled} onTime={setDailyAt} onSave={() => {
              setScheduled(true);
              setLibrary((items) => items.map((item, index) => index === 0 ? { ...item, dailyAt } : item));
            }} onHome={() => setPhase("home")} />
          ) : null}
        </div>
      ) : null}
      {phase === "demo" && draft ? (
        <DemoRun findings={findings} shown={shown} stopped={stopped} onStop={() => setStopped(true)} onBack={() => setPhase("wizard")} />
      ) : null}
      {phase === "result" && demo && draft ? (
        <ResultPage
          passed={demo.passed}
          findings={findings}
          onSave={saveAndSchedule}
          onAgain={showDemo}
          onEdit={() => { setPhase("wizard"); setStepIndex(2); }}
        />
      ) : null}
    </section>
  );
}

function readPace(fallback: number): number {
  try {
    const value = Number(window.sessionStorage.getItem("workbench.author.pace"));
    if (Number.isFinite(value) && value >= 0) return value;
  } catch {
    return fallback;
  }
  return fallback;
}

function readFake(): "off" | "pass" | "fail" {
  try {
    const value = window.sessionStorage.getItem("workbench.author.fake");
    if (value === "1" || value === "pass") return "pass";
    if (value === "fail") return "fail";
  } catch {
    return "off";
  }
  return "off";
}

function Home({ library, onNew }: { library: SavedTest[]; onNew: () => void }) {
  if (library.length === 0) {
    return (
      <div className="wizard-card" aria-label="开始">
        <h2>还没有测试</h2>
        <p>先选择要测的程序。连上之后，用一句话说明要检查什么，再看着它做一遍。</p>
        <div className="wizard-actions">
          <button type="button" className="btn-accent" onClick={onNew}>选择程序</button>
        </div>
      </div>
    );
  }
  return (
    <div className="wizard-card" aria-label="已保存的测试">
      <div className="wizard-actions">
        <h2>已保存的测试</h2>
        <button type="button" className="btn-accent" onClick={onNew}>新建一个测试</button>
      </div>
      <ul className="saved-list">
        {library.map((item) => (
          <li key={item.id}>
            <strong>{item.name}</strong>
            <span>{item.passed ? "通过" : "没通过"}</span>
            <span>{item.at}</span>
            <span>下次 {item.dailyAt}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function AppStep({ appName, connected, status, hint, recent, onChoose, onNext }: {
  appName: string; connected: boolean; status: string; hint: string; recent: string[];
  onChoose: (name: string) => void; onNext: () => void;
}) {
  return (
    <div className="wizard-card" aria-label="选择程序">
      <h2>选择要测的程序</h2>
      <p>选好后会由这里打开并连上。打不开时，换一个可以自己运行的程序再试。</p>
      <label className="btn">
        浏览
        <input aria-label="浏览程序" className="file-input" type="file" accept=".exe,.app,application/octet-stream" onChange={(event) => {
          const file = event.target.files?.[0];
          if (file) onChoose(file.name);
        }} />
      </label>
      <p>最近用过的</p>
      <ul className="case-list" aria-label="最近用过的程序">
        {recent.map((name) => (
          <li key={name}><button type="button" className={name === appName ? "is-on" : ""} onClick={() => onChoose(name)}>{name}</button></li>
        ))}
      </ul>
      <p role="status">{status}。{hint}</p>
      {!connected && appName ? <button type="button" className="btn" onClick={() => onChoose("")}>重新选择</button> : null}
      <div className="wizard-actions">
        <button type="button" className="btn-accent" disabled={!connected} onClick={onNext}>下一步</button>
      </div>
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
      <div className="wizard-actions">
        <button type="button" className="btn" onClick={onBack}>上一步</button>
        <button type="button" className="btn-accent" disabled={goal.trim().length === 0} onClick={onNext}>下一步</button>
      </div>
    </div>
  );
}

function DraftStep({ left, keyReady, draft, notice, correction, onCorrection, onRetry, onDemo, onBack }: {
  left: number; keyReady: boolean; draft: ExecutorCase | null; notice: string; correction: string;
  onCorrection: (value: string) => void; onRetry: () => void; onDemo: () => void; onBack: () => void;
}) {
  const plan = draft ? plainPlan(draft) : null;
  return (
    <div className="wizard-card" aria-label="看一遍">
      <h2>看它准备做什么</h2>
      <p className="tiny">今天还能生成 {left} 次。{keyReady ? <a href="/settings">去设置</a> : null}</p>
      {!keyReady ? <p role="status">{authorMessages.missingKey}<a href="/settings">去设置</a></p> : null}
      {notice ? <p role="status">{notice}</p> : null}
      {plan ? (
        <div className="plan" aria-label="检查清单">
          <h3>{draft!.name}</h3>
          <div>
            <h4>它会做</h4>
            <ul>{plan.does.map((line) => <li key={line}>{line}</li>)}</ul>
          </div>
          <div>
            <h4>它会核对</h4>
            <ul>{plan.checks.map((line) => <li key={line}>{line}</li>)}</ul>
          </div>
          <details>
            <summary>详情</summary>
            <pre>{draft!.script}</pre>
          </details>
        </div>
      ) : null}
      {keyReady ? (
        <label>
          哪里不对？
          <textarea aria-label="哪里不对" value={correction} placeholder="例如：旁边的牙不应该变" onChange={(event) => onCorrection(event.target.value)} />
        </label>
      ) : null}
      <div className="wizard-actions">
        <button type="button" className="btn" onClick={onBack}>上一步</button>
        {keyReady ? <button type="button" className="btn" onClick={onRetry}>重新生成</button> : null}
        <button type="button" className="btn-accent" disabled={!draft} onClick={onDemo}>演示一遍</button>
      </div>
    </div>
  );
}

function DemoRun({ findings, shown, stopped, onStop, onBack }: {
  findings: Finding[]; shown: number; stopped: boolean; onStop: () => void; onBack: () => void;
}) {
  return (
    <div className="wizard-card" aria-label="演示">
      <h2>正在这个程序里做一遍</h2>
      <ol className="live-list" aria-label="演示进度">
        {findings.slice(0, shown).map((item) => (
          <li key={item.sentence} data-state={item.ok ? "ok" : "bad"}>
            <span>{item.ok ? "完成" : "没通过"}</span>
            <span>{item.sentence}</span>
          </li>
        ))}
      </ol>
      {shown < findings.length && !stopped ? <p role="status">正在检查下一项。</p> : null}
      {stopped ? <p role="status">已停下。可以回去改一句，再重新生成。</p> : null}
      <div className="wizard-actions">
        <button type="button" className="btn" onClick={onBack}>返回修改</button>
        {shown < findings.length && !stopped ? <button type="button" className="btn" onClick={onStop}>停止</button> : null}
      </div>
    </div>
  );
}

function ScheduleStep({ dailyAt, scheduled, onTime, onSave, onHome }: {
  dailyAt: string; scheduled: boolean; onTime: (value: string) => void; onSave: () => void; onHome: () => void;
}) {
  return (
    <div className="wizard-card" aria-label="每天重跑">
      <h2>每天自动再跑一遍</h2>
      <p>到点只重跑这一份，不会再问。</p>
      <label>
        每天重跑的时间
        <input aria-label="每天重跑的时间" type="time" value={dailyAt} onChange={(event) => onTime(event.target.value)} />
      </label>
      <div className="wizard-actions">
        <button type="button" className="btn" onClick={onHome}>回到首页</button>
        <button type="button" className="btn-accent" onClick={onSave}>保存安排</button>
      </div>
      {scheduled ? <p role="status">已记下。到 {dailyAt} 会再跑一遍，不会再问。</p> : null}
    </div>
  );
}

function ResultPage({ passed, findings, onSave, onAgain, onEdit }: {
  passed: boolean; findings: Finding[]; onSave: () => void; onAgain: () => void; onEdit: () => void;
}) {
  return (
    <div className="wizard-card result-page" aria-label="结果">
      <p className={passed ? "result-mark pass" : "result-mark fail"}>{passed ? "通过" : "没通过"}</p>
      <p role="status">{resultSentence(passed)}</p>
      <div className="frames" aria-label="前后画面">
        <figure>
          <svg viewBox="0 0 320 200" preserveAspectRatio="xMidYMid slice" role="img" aria-label="之前">
            <rect className="frame-bg" width="320" height="200" />
            <rect className="piece" x="70" y="30" width="180" height="140" />
          </svg>
          <figcaption>之前。只留在这台电脑上。</figcaption>
        </figure>
        <figure>
          <svg viewBox="0 0 320 200" preserveAspectRatio="xMidYMid slice" role="img" aria-label="之后">
            <rect className="frame-bg" width="320" height="200" />
            <rect className="piece" x="110" y="60" width="100" height="80" />
            <rect className="changed" x="100" y="50" width="120" height="100" />
          </svg>
          <figcaption>之后。框出变小的部分。只留在这台电脑上。</figcaption>
        </figure>
      </div>
      <ul className="finding-list" aria-label="核对结果">
        {findings.map((item) => (
          <li key={item.sentence} data-state={item.ok ? "ok" : "bad"}>
            <strong>{item.ok ? "通过" : "没通过"}</strong>
            <span>{item.sentence}</span>
            {item.expected ? <span>{item.expected}</span> : null}
            {item.seen ? <span>{item.seen}</span> : null}
          </li>
        ))}
      </ul>
      <details>
        <summary>详情</summary>
        <pre>{findings.map((item) => `${item.ok ? "ok" : "bad"} ${item.sentence}`).join("\n")}</pre>
      </details>
      <div className="wizard-actions">
        {passed ? <button type="button" className="btn-accent" onClick={onSave}>保存并设置每天重跑</button> : (
          <>
            <button type="button" className="btn" onClick={onEdit}>修改检查</button>
            <button type="button" className="btn-accent" onClick={onAgain}>重新演示</button>
          </>
        )}
      </div>
    </div>
  );
}
