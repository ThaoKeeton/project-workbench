import { useEffect, useRef, useState } from "react";
import "./studio.css";
import {
  authorMessages,
  appFromFileName,
  authorPrompt,
  caseFromScript,
  connectApp,
  dismissHint,
  failingReplayScript,
  fixedReplayScript,
  formatWhen,
  hintDismissed,
  humanFindings,
  nextDailyInstant,
  plainPlan,
  readAuthorFake,
  readDailyCap,
  replay,
  resultSentence,
  sampleAppName,
  sampleApps,
  sampleDom,
  sampleLibrary,
  sceneSnapshotText,
  type ConnectView,
  type ExecutorCase,
  type ExecutorRun,
  type Finding,
  type SavedTestRecord,
  type TestApp,
} from "./model";

export type ScriptCompleter = (prompt: string) => Promise<{ text: string; cost: number }>;

const stepNames = ["选择程序", "说一句", "看一遍", "每天重跑"] as const;

type SavedTest = SavedTestRecord;

export function ExecutorStudio({ complete, stepDelay = 160 }: { complete?: ScriptCompleter; stepDelay?: number }) {
  const [resumed] = useState(readWizardDraft);
  const resumedApp = sampleApps().find((item) => item.id === resumed.appId) ?? null;
  const [phase, setPhase] = useState<"home" | "wizard" | "demo" | "result">(resumed.resume ? "wizard" : "home");
  const [stepIndex, setStepIndex] = useState(resumed.resume ? resumed.step : 0);
  const [picked, setPicked] = useState<TestApp | null>(resumed.resume ? resumedApp : null);
  const [attempt, setAttempt] = useState(1);
  const [relaunch, setRelaunch] = useState(false);
  const [recent, setRecent] = useState<TestApp[]>(() => sampleApps().filter((item) => item.recent));
  const [goal, setGoal] = useState(resumed.resume ? resumed.goal : "");
  const [correction, setCorrection] = useState("");
  const [cap] = useState(readDailyCap);
  const [spent, setSpent] = useState(0);
  const [draft, setDraft] = useState<ExecutorCase | null>(null);
  const [notice, setNotice] = useState("");
  const [demo, setDemo] = useState<ExecutorRun | null>(null);
  const [findings, setFindings] = useState<Finding[]>([]);
  const [shown, setShown] = useState(0);
  const [stopped, setStopped] = useState(false);
  const [library, setLibrary] = useState<SavedTest[]>(() => (readSamples() ? sampleLibrary() : []));
  const [opened, setOpened] = useState<SavedTest | null>(null);
  const [relaunchNote, setRelaunchNote] = useState("");
  const [dailyAt, setDailyAt] = useState("09:00");
  const [scheduled, setScheduled] = useState(false);
  const keyReady = Boolean(complete) || readAuthorFake() !== "off";
  const connection = connectApp(picked, { attempt, relaunch });
  const pace = readPace(stepDelay);
  const left = Math.max(0, cap - spent);

  useEffect(() => {
    rememberWizard(stepIndex, picked?.id ?? "", goal);
  }, [stepIndex, picked, goal]);

  useEffect(() => {
    if (phase !== "demo" || stopped || !demo) return;
    if (shown >= findings.length) {
      const timer = window.setTimeout(() => setPhase("result"), pace);
      return () => window.clearTimeout(timer);
    }
    const timer = window.setTimeout(() => setShown((count) => count + 1), pace);
    return () => window.clearTimeout(timer);
  }, [phase, shown, stopped, pace, findings.length, demo]);

  const choose = (next: TestApp | null, options: { attempt?: number; relaunch?: boolean } = {}) => {
    if (!next) {
      setPicked(null);
      setAttempt(1);
      setRelaunch(false);
      return;
    }
    const nextAttempt = options.attempt ?? 1;
    const nextRelaunch = Boolean(options.relaunch);
    setPicked(next);
    setAttempt(nextAttempt);
    setRelaunch(nextRelaunch);
    if (connectApp(next, { attempt: nextAttempt, relaunch: nextRelaunch }).state === "connected") {
      setRecent((items) => [next, ...items.filter((item) => item.id !== next.id)].slice(0, 4));
    }
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
    const result = complete ? await complete(prompt) : { text: readAuthorFake() === "fail" ? failingReplayScript : fixedReplayScript, cost: 0 };
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
      lastRun: new Date().toISOString(),
      dailyAt,
      paused: false,
      script: draft.script ?? "",
      appId: picked?.id ?? "sample",
      appName: picked?.name ?? sampleAppName,
    }, ...library.filter((item) => item.id !== draft.id)]);
    setScheduled(false);
    setStepIndex(3);
    setPhase("wizard");
  };

  const openSaved = (item: SavedTest, rerun: boolean) => {
    const saved = caseFromScript(item.script);
    if (!saved) return;
    const result = replay(saved, sampleDom);
    const next = rerun ? { ...item, passed: result.passed, lastRun: new Date().toISOString() } : item;
    if (rerun) setLibrary((items) => items.map((entry) => entry.id === item.id ? next : entry));
    setRelaunchNote(rerun ? `已重新打开「${item.appName || sampleAppName}」。` : "");
    setDraft(saved);
    setDemo(result);
    setFindings(humanFindings(saved, result));
    setOpened(next);
    setPhase("result");
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
        <Home
          library={library}
          onNew={() => { setOpened(null); setStepIndex(0); setPhase("wizard"); }}
          onOpen={(item) => openSaved(item, false)}
          onRun={(item) => openSaved(item, true)}
          onRename={(id, name) => setLibrary((items) => items.map((item) => item.id === id ? { ...item, name } : item))}
          onPause={(id) => setLibrary((items) => items.map((item) => item.id === id ? { ...item, paused: !item.paused } : item))}
          onDelete={(id) => setLibrary((items) => items.filter((item) => item.id !== id))}
        />
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
          {stepIndex === 0 ? (
            <AppStep
              picked={picked}
              connection={connection}
              recent={recent}
              installed={sampleApps().filter((item) => !recent.some((entry) => entry.id === item.id))}
              onChoose={choose}
              onNext={() => setStepIndex(1)}
            />
          ) : null}
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
              onLeave={() => rememberWizard(2, picked?.id ?? "", goal)}
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
          onSave={opened ? () => setPhase("home") : saveAndSchedule}
          saveLabel={opened ? "返回首页" : "保存并设置每天重跑"}
          note={relaunchNote}
          onAgain={opened ? () => openSaved(opened, true) : showDemo}
          onEdit={() => { setOpened(null); setPhase("wizard"); setStepIndex(2); }}
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

function readSamples(): boolean {
  try {
    return window.sessionStorage.getItem("workbench.author.samples") === "1";
  } catch {
    return false;
  }
}

function readWizardDraft(): { resume: boolean; step: number; appId: string; goal: string } {
  try {
    const resume = window.sessionStorage.getItem("workbench.wizard.resume") === "1";
    const step = Number(window.sessionStorage.getItem("workbench.wizard.step") ?? "0");
    const appId = window.sessionStorage.getItem("workbench.wizard.app") ?? "";
    const goal = window.sessionStorage.getItem("workbench.wizard.goal") ?? "";
    if (resume) window.sessionStorage.removeItem("workbench.wizard.resume");
    return { resume, step: Number.isFinite(step) && step >= 0 && step <= 3 ? step : 0, appId, goal };
  } catch {
    return { resume: false, step: 0, appId: "", goal: "" };
  }
}

function rememberWizard(step: number, appId: string, goal: string) {
  try {
    window.sessionStorage.setItem("workbench.wizard.step", String(step));
    window.sessionStorage.setItem("workbench.wizard.app", appId);
    window.sessionStorage.setItem("workbench.wizard.goal", goal);
  } catch {
    return;
  }
}

function Home({ library, onNew, onOpen, onRun, onRename, onPause, onDelete }: {
  library: SavedTest[];
  onNew: () => void;
  onOpen: (item: SavedTest) => void;
  onRun: (item: SavedTest) => void;
  onRename: (id: string, name: string) => void;
  onPause: (id: string) => void;
  onDelete: (id: string) => void;
}) {
  const [filter, setFilter] = useState<"all" | "fail" | "pass">("all");
  const [menu, setMenu] = useState<string | null>(null);
  const [renaming, setRenaming] = useState<SavedTest | null>(null);
  const [renameValue, setRenameValue] = useState("");
  const [removing, setRemoving] = useState<SavedTest | null>(null);
  const [showHint, setShowHint] = useState(() => !hintDismissed());
  if (library.length === 0) {
    return (
      <div className="wizard-card" aria-label="开始">
        <h2>还没有测试</h2>
        {showHint ? (
          <div className="first-hint" role="note" aria-label="第一次使用">
            <p>先选一个程序。</p>
            <p>用一句话说明要检查什么。</p>
            <p>看它做一遍，对了再保存。</p>
            <button type="button" className="btn" onClick={() => { dismissHint(); setShowHint(false); }}>知道了</button>
          </div>
        ) : <p>先选择要测的程序，再用一句话说明要检查什么。</p>}
        <div className="wizard-actions">
          <button type="button" className="btn-accent" onClick={onNew}>选择程序</button>
        </div>
      </div>
    );
  }
  const visible = library.filter((item) => filter === "all" || (filter === "pass" ? item.passed : !item.passed));
  return (
    <div className="wizard-card" aria-label="已保存的测试">
      <div className="wizard-actions">
        <h2>已保存的测试</h2>
        <button type="button" className="btn-accent" onClick={onNew}>新建一个测试</button>
      </div>
      {library.length > 5 ? (
        <div className="filter-row" role="group" aria-label="筛选">
          {([["all", "全部"], ["fail", "没通过"], ["pass", "通过"]] as const).map(([value, label]) => (
            <button key={value} type="button" className={filter === value ? "is-on" : ""} aria-pressed={filter === value} onClick={() => setFilter(value)}>{label}</button>
          ))}
        </div>
      ) : null}
      <ul className="saved-list">
        {visible.map((item) => {
          const next = item.paused ? "每天重跑已暂停" : `下次 ${formatWhen(nextDailyInstant(item.dailyAt))}`;
          return (
            <li key={item.id} className={item.passed ? "" : "is-fail"}>
              <button type="button" className="row-open" onClick={() => onOpen(item)}>
                <strong>{item.name}</strong>
                <span className="result-label">{item.passed ? "通过" : "没通过"}</span>
                <span>上次 {formatWhen(new Date(item.lastRun))}</span>
                <span>{next}</span>
              </button>
              <button type="button" className="btn" onClick={() => onRun(item)}>现在跑一次</button>
              <div className="more-menu">
                <button type="button" className="btn" aria-expanded={menu === item.id} aria-label={`${item.name} 更多`} onClick={() => setMenu(menu === item.id ? null : item.id)}>…</button>
                {menu === item.id ? (
                  <div role="menu">
                    <button type="button" role="menuitem" onClick={() => { setRenaming(item); setRenameValue(item.name); setMenu(null); }}>重命名</button>
                    <button type="button" role="menuitem" onClick={() => { onPause(item.id); setMenu(null); }}>{item.paused ? "恢复每天重跑" : "暂停每天重跑"}</button>
                    <button type="button" role="menuitem" onClick={() => { setRemoving(item); setMenu(null); }}>删除</button>
                  </div>
                ) : null}
              </div>
            </li>
          );
        })}
      </ul>
      {renaming ? (
        <div role="dialog" aria-label="重命名">
          <label>
            新名称
            <input aria-label="新名称" value={renameValue} onChange={(event) => setRenameValue(event.target.value)} />
          </label>
          <div className="wizard-actions">
            <button type="button" className="btn" onClick={() => setRenaming(null)}>取消</button>
            <button type="button" className="btn-accent" onClick={() => { onRename(renaming.id, renameValue.trim() || renaming.name); setRenaming(null); }}>保存名称</button>
          </div>
        </div>
      ) : null}
      {removing ? (
        <div role="dialog" aria-label="确认删除">
          <p>删除「{removing.name}」？删掉之后不能恢复。</p>
          <div className="wizard-actions">
            <button type="button" className="btn" onClick={() => setRemoving(null)}>取消</button>
            <button type="button" className="btn-accent" onClick={() => { onDelete(removing.id); setRemoving(null); }}>删除</button>
          </div>
        </div>
      ) : null}
    </div>
  );
}

function AppStep({ picked, connection, recent, installed, onChoose, onNext }: {
  picked: TestApp | null;
  connection: ConnectView;
  recent: TestApp[];
  installed: TestApp[];
  onChoose: (app: TestApp | null, options?: { attempt?: number; relaunch?: boolean }) => void;
  onNext: () => void;
}) {
  const [confirm, setConfirm] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const connected = connection.state === "connected";
  return (
    <div className="wizard-card" aria-label="选择程序">
      <h2>选择要测的程序</h2>
      <p>从最近用过的里面选，浏览一个程序，或从这台电脑上已安装的里面选。选中后由这里打开并连上。</p>
      <button type="button" className="btn" onClick={() => fileRef.current?.click()}>浏览…</button>
      <input ref={fileRef} aria-label="浏览程序" className="file-input" type="file" accept=".exe,.app,application/octet-stream" onChange={(event) => {
        const file = event.target.files?.[0];
        const next = file ? appFromFileName(file.name) : null;
        if (next) onChoose(next);
      }} />
      <h3>最近用过的</h3>
      <ul className="app-grid" aria-label="最近用过的程序">
        {recent.map((app) => <AppButton key={app.id} app={app} selected={picked?.id === app.id} onChoose={onChoose} />)}
      </ul>
      <h3>这台电脑上的程序</h3>
      <ul className="app-grid" aria-label="已安装的程序">
        {installed.map((app) => <AppButton key={app.id} app={app} selected={picked?.id === app.id} onChoose={onChoose} />)}
      </ul>
      <p role="status">{connection.status}</p>
      {connection.detail ? <p>{connection.detail}</p> : null}
      {connected ? <WindowPreview objects={connection.objects} /> : null}
      {connection.state === "busy" && picked ? (
        <button type="button" className="btn" onClick={() => setConfirm(true)}>关掉并重新打开</button>
      ) : null}
      {connection.state === "timeout" && picked ? (
        <button type="button" className="btn" onClick={() => onChoose(picked, { attempt: 2 })}>再试一次</button>
      ) : null}
      {connection.state === "unsupported" ? <button type="button" className="btn" onClick={() => onChoose(null)}>重新选择</button> : null}
      {confirm && picked ? (
        <div role="dialog" aria-label="确认重新打开">
          <p>关掉并重新打开「{picked.name}」？正在用的窗口会关掉。</p>
          <div className="wizard-actions">
            <button type="button" className="btn" onClick={() => setConfirm(false)}>取消</button>
            <button type="button" className="btn-accent" onClick={() => { setConfirm(false); onChoose(picked, { relaunch: true }); }}>关掉并重新打开</button>
          </div>
        </div>
      ) : null}
      <div className="wizard-actions">
        <button type="button" className="btn-accent" disabled={!connected} onClick={onNext}>下一步</button>
      </div>
    </div>
  );
}

function AppButton({ app, selected, onChoose }: { app: TestApp; selected: boolean; onChoose: (app: TestApp) => void }) {
  return (
    <li>
      <button type="button" className={selected ? "is-on" : ""} onClick={() => onChoose(app)}>
        <span className="app-icon" aria-hidden="true">{app.name.slice(0, 1)}</span>
        <span>{app.name}</span>
      </button>
    </li>
  );
}

function WindowPreview({ objects }: { objects: number | null }) {
  return (
    <div className="app-preview">
      <svg viewBox="0 0 220 124" role="img" aria-label="程序画面">
        <rect className="frame-bg" width="220" height="124" />
        {objects !== null && objects > 0 ? (
          <>
            <rect className="piece" x="36" y="28" width="70" height="68" />
            <circle className="piece" cx="150" cy="62" r="22" />
          </>
        ) : <rect className="piece" x="24" y="36" width="172" height="12" />}
      </svg>
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

const aiSettingsHref = "/settings/ai?return=/plugin/runner";

function DraftStep({ left, keyReady, draft, notice, correction, onCorrection, onRetry, onDemo, onBack, onLeave }: {
  left: number; keyReady: boolean; draft: ExecutorCase | null; notice: string; correction: string;
  onCorrection: (value: string) => void; onRetry: () => void; onDemo: () => void; onBack: () => void; onLeave: () => void;
}) {
  const plan = draft ? plainPlan(draft) : null;
  return (
    <div className="wizard-card" aria-label="看一遍">
      <h2>看它准备做什么</h2>
      <p className="tiny">今天还能生成 {left} 次。{keyReady ? <a href={aiSettingsHref} onClick={onLeave}>去设置</a> : null}</p>
      {!keyReady ? <p role="status">{authorMessages.missingKey}<a href={aiSettingsHref} onClick={onLeave}>去设置</a></p> : null}
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

function ResultPage({ passed, findings, onSave, saveLabel, onAgain, onEdit, note }: {
  passed: boolean; findings: Finding[]; onSave: () => void; saveLabel: string; onAgain: () => void; onEdit: () => void; note?: string;
}) {
  return (
    <div className="wizard-card result-page" aria-label="结果">
      <p className={passed ? "result-mark pass" : "result-mark fail"}>{passed ? "通过" : "没通过"}</p>
      <p role="status">{resultSentence(passed)}</p>
      {note ? <p>{note}</p> : null}
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
        {passed ? <button type="button" className="btn-accent" onClick={onSave}>{saveLabel}</button> : (
          <>
            <button type="button" className="btn" onClick={onEdit}>修改检查</button>
            <button type="button" className="btn-accent" onClick={onAgain}>重新演示</button>
          </>
        )}
      </div>
    </div>
  );
}
