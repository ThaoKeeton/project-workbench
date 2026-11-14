import { useEffect, useRef, useState } from "react";
import "./studio.css";
import {
  authorMessages,
  appFromFileName,
  authorPrompt,
  caseFromScript,
  connectApp,
  failingReplayScript,
  fixedReplayScript,
  allWeekdays,
  authorKeyReady,
  formatWhen,
  humanFindings,
  missedBecauseOff,
  nextDailyInstant,
  outcomeOf,
  outcomeWord,
  pageChangedFindings,
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
  type RunRecord,
  type SavedTestRecord,
  type TestApp,
} from "./model";

export type ScriptCompleter = (prompt: string) => Promise<{ text: string; cost: number }>;

const stepNames = ["选择程序", "说一句", "看一遍", "每天重跑"] as const;

type SavedTest = SavedTestRecord;

export function ExecutorStudio({ complete, stepDelay = 160 }: { complete?: ScriptCompleter; stepDelay?: number }) {
  const [resumed] = useState(readWizardDraft);
  const resumedApp = sampleApps().find((item) => item.id === resumed.appId) ?? null;
  const [phase, setPhase] = useState<"home" | "wizard" | "demo" | "result" | "rerecord">(resumed.resume ? "wizard" : "home");
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
  const [weekdays, setWeekdays] = useState<number[]>(allWeekdays);
  const [autostart, setAutostart] = useState(readAutostart);
  const [missedNote, setMissedNote] = useState("");
  const [repairNote, setRepairNote] = useState("");
  const [rerecordStep, setRerecordStep] = useState(0);
  const [busy, setBusy] = useState<"" | "open" | "write">("");
  const cancelToken = useRef(0);
  const [scheduled, setScheduled] = useState(false);
  const fakeMode = readAuthorFake();
  const keyReady = authorKeyReady(fakeMode, Boolean(complete));
  const connection = connectApp(picked, { attempt, relaunch });
  const left = Math.max(0, cap - spent);

  useEffect(() => {
    rememberWizard(stepIndex, picked?.id ?? "", goal);
  }, [stepIndex, picked, goal]);

  useEffect(() => {
    if (!readBoot()) return;
    const now = readClock();
    const due = library.some((item) => !item.paused && missedBecauseOff(new Date(item.lastRun), item.dailyAt, item.weekdays ?? allWeekdays, now));
    if (due) setMissedNote("上次到点时电脑关着，刚补跑了一次。");
  }, [library]);

  useEffect(() => {
    if (phase !== "demo" || stopped || !demo) return;
    const pace = readPace(stepDelay);
    if (shown >= findings.length) {
      const timer = window.setTimeout(() => setPhase("result"), pace);
      return () => window.clearTimeout(timer);
    }
    const timer = window.setTimeout(() => setShown((count) => count + 1), pace);
    return () => window.clearTimeout(timer);
  }, [phase, shown, stopped, stepDelay, findings.length, demo]);

  const cancelBusy = () => {
    cancelToken.current += 1;
    setBusy("");
  };

  const choose = (next: TestApp | null, options: { attempt?: number; relaunch?: boolean } = {}) => {
    const token = ++cancelToken.current;
    void (async () => {
      if (!next) {
        setPicked(null);
        setAttempt(1);
        setRelaunch(false);
        setBusy("");
        return;
      }
      const nextAttempt = options.attempt ?? 1;
      const nextRelaunch = Boolean(options.relaunch);
      const preview = connectApp(next, { attempt: nextAttempt, relaunch: nextRelaunch });
      if (preview.state === "connected" && readWait()) {
        setBusy("open");
        const ok = await holdUntilSettled(token, () => cancelToken.current);
        if (!ok) {
          setBusy("");
          return;
        }
      }
      if (token !== cancelToken.current) return;
      setBusy("");
      setPicked(next);
      setAttempt(nextAttempt);
      setRelaunch(nextRelaunch);
      if (preview.state === "connected") {
        setRecent((items) => [next, ...items.filter((item) => item.id !== next.id)].slice(0, 4));
      }
    })();
  };

  const draftCase = async (note = correction, goalText = goal) => {
    const token = ++cancelToken.current;
    setDraft(null);
    setDemo(null);
    if (goalText.trim().length === 0) {
      setNotice(authorMessages.empty);
      return;
    }
    if (!keyReady) return;
    if (spent >= cap) {
      setNotice(authorMessages.overBudget);
      return;
    }
    setBusy("write");
    const ok = await holdUntilSettled(token, () => cancelToken.current);
    if (!ok) {
      setBusy("");
      return;
    }
    const prompt = authorPrompt([goalText, note].filter((part) => part.trim()).join("\n"), sampleDom, sceneSnapshotText);
    const result = complete ? await complete(prompt) : { text: readAuthorFake() === "fail" ? failingReplayScript : fixedReplayScript, cost: 0 };
    setSpent((value) => value + result.cost);
    const authored = caseFromScript(result.text);
    if (!authored) {
      setBusy("");
      setNotice(authorMessages.invalid);
      return;
    }
    if (token !== cancelToken.current) return;
    setBusy("");
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
      weekdays,
      history: [{ at: new Date().toISOString(), outcome: demo.passed ? "pass" : "fail", reason: demo.passed ? "通过" : "没通过", image: true }],
    }, ...library.filter((item) => item.id !== draft.id)]);
    setScheduled(false);
    setStepIndex(3);
    setPhase("wizard");
  };

  const openSaved = (item: SavedTest, rerun: boolean) => {
    const saved = caseFromScript(item.script);
    if (!saved) return;
    const changed = rerun ? readAuthorFake() === "changed" : item.history.at(-1)?.outcome === "needs-update";
    const result = replay(saved, sampleDom);
    const findingsForRun = changed ? pageChangedFindings(saved) : humanFindings(saved, result);
    const outcome = changed ? "needs-update" as const : outcomeOf(findingsForRun);
    const next = rerun ? {
      ...item,
      passed: outcome === "pass",
      lastRun: new Date().toISOString(),
      history: [...(item.history ?? []), { at: new Date().toISOString(), outcome, reason: findingsForRun[0]?.sentence ?? "", image: true }],
    } : item;
    if (rerun) setLibrary((items) => items.map((entry) => entry.id === item.id ? next : entry));
    setRelaunchNote(rerun ? `已重新打开「${item.appName || sampleAppName}」，跑完后已关掉。` : "");
    setDraft(saved);
    setDemo(changed ? { ...result, passed: false } : result);
    setFindings(rerun ? findingsForRun : humanFindings(saved, result));
    setOpened(next);
    setPhase("result");
  };

  return (
    <section className="executor" aria-label="自动测试">
      <header className="executor-head">
        <div>
          <h1>自动测试</h1>
          {phase === "result" || (phase === "home" && library.length === 0) ? null : <p>选一个程序，用一句话说要检查什么，再看它做一遍。</p>}
        </div>
      </header>
      {phase === "home" ? (
        <Home
          library={library}
          missedNote={missedNote}
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
              busy={busy === "open"}
              onChoose={choose}
              onCancel={cancelBusy}
              onNext={() => setStepIndex(1)}
            />
          ) : null}
          {stepIndex === 1 ? <GoalStep goal={goal} onGoal={setGoal} onBack={() => setStepIndex(0)} onNext={() => { setStepIndex(2); void draftCase(""); }} /> : null}
          {stepIndex === 2 ? (
            <DraftStep
              left={left}
              keyReady={keyReady}
              draft={draft}
              notice={repairNote || notice}
              correction={correction}
              onCorrection={setCorrection}
              onRetry={() => void draftCase(correction)}
              onDemo={showDemo}
              onBack={() => setStepIndex(1)}
              onCancel={() => { cancelBusy(); setStepIndex(1); }}
              writing={busy === "write"}
              onLeave={() => rememberWizard(2, picked?.id ?? "", goal)}
            />
          ) : null}
          {stepIndex === 3 ? (
            <ScheduleStep dailyAt={dailyAt} weekdays={weekdays} autostart={autostart} scheduled={scheduled} onTime={setDailyAt} onWeekdays={setWeekdays} onAutostart={(value) => { setAutostart(value); writeAutostart(value); }} onSave={() => {
              setScheduled(true);
              setLibrary((items) => items.map((item, index) => index === 0 ? { ...item, dailyAt, weekdays } : item));
            }} onHome={() => setPhase("home")} />
          ) : null}
        </div>
      ) : null}
      {phase === "demo" && draft ? (
        <DemoRun findings={findings} shown={shown} stopped={stopped} onStop={() => setStopped(true)} onBack={() => setPhase("wizard")} />
      ) : null}
      {phase === "rerecord" && draft ? (
        <Rerecord
          appName={opened?.appName || picked?.name || sampleAppName}
          sentence={findings.find((item) => item.kind === "missing")?.sentence ?? "第 2 步找不到「开始」按钮"}
          step={rerecordStep}
          onAdvance={() => setRerecordStep((value) => value + 1)}
          onSave={() => {
            if (opened) {
              const at = new Date().toISOString();
              const history = [...opened.history, { at, outcome: "pass" as const, reason: "已按刚才的演示更新", image: true }];
              const next = { ...opened, passed: true, lastRun: at, history };
              setOpened(next);
              setLibrary((items) => items.map((item) => item.id === opened.id ? next : item));
            }
            setPhase("home");
          }}
        />
      ) : null}
      {phase === "result" && demo && draft ? (
        <ResultPage
          passed={demo.passed}
          findings={findings}
          onSave={opened ? () => setPhase("home") : saveAndSchedule}
          saveLabel={opened ? "返回首页" : "保存并设置每天重跑"}
          note={relaunchNote}
          history={opened?.history ?? []}
          keyReady={keyReady}
          canRepair={left > 0}
          onRepair={() => { const text = goal || draft.name; setRepairNote("这是建议的修改，还没保存。"); setGoal(text); setStepIndex(2); setPhase("wizard"); void draftCase("", text); }}
          onRerecord={() => { setRerecordStep(0); setPhase("rerecord"); }}
          onClearImages={() => {
            if (!opened) return;
            const history = (opened.history ?? []).map((entry) => ({ ...entry, image: false }));
            setOpened({ ...opened, history });
            setLibrary((items) => items.map((item) => item.id === opened.id ? { ...item, history } : item));
          }}
          onAgain={opened ? () => openSaved(opened, true) : showDemo}
          onEdit={() => { setOpened(null); setPhase("wizard"); setStepIndex(2); }}
        />
      ) : null}
    </section>
  );
}

function readWait(): boolean {
  try {
    return window.sessionStorage.getItem("workbench.author.wait") === "1";
  } catch {
    return false;
  }
}

function holdUntilSettled(token: number, current: () => number): Promise<boolean> {
  if (!readWait()) return Promise.resolve(token === current());
  return new Promise((resolve) => {
    const timer = window.setInterval(() => {
      if (token !== current()) {
        window.clearInterval(timer);
        resolve(false);
        return;
      }
      if (!readWait()) {
        window.clearInterval(timer);
        resolve(true);
      }
    }, 30);
  });
}

function readPace(fallback: number): number {
  try {
    const raw = window.sessionStorage.getItem("workbench.author.pace");
    if (raw === null || raw === "") return fallback;
    const value = Number(raw);
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

function readClock(): Date {
  try {
    const raw = window.sessionStorage.getItem("workbench.author.now");
    if (raw) {
      const date = new Date(raw);
      if (!Number.isNaN(date.getTime())) return date;
    }
  } catch {
    return new Date();
  }
  return new Date();
}

function readBoot(): boolean {
  try {
    return window.sessionStorage.getItem("workbench.author.boot") === "1";
  } catch {
    return false;
  }
}

function readAutostart(): boolean {
  try {
    return window.localStorage.getItem("workbench.author.autostart") === "1";
  } catch {
    return false;
  }
}

function writeAutostart(enabled: boolean) {
  window.localStorage.setItem("workbench.author.autostart", enabled ? "1" : "0");
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

function Home({ library, missedNote, onNew, onOpen, onRun, onRename, onPause, onDelete }: {
  library: SavedTest[];
  missedNote: string;
  onNew: () => void;
  onOpen: (item: SavedTest) => void;
  onRun: (item: SavedTest) => void;
  onRename: (id: string, name: string) => void;
  onPause: (id: string) => void;
  onDelete: (id: string) => void;
}) {
  const [filter, setFilter] = useState<"all" | "fail" | "update" | "pass">(readListFilter);
  const [menu, setMenu] = useState<string | null>(null);
  const [renaming, setRenaming] = useState<SavedTest | null>(null);
  const [renameValue, setRenameValue] = useState("");
  const [removing, setRemoving] = useState<SavedTest | null>(null);
  useEffect(() => {
    if (!renaming && !removing) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      setRenaming(null);
      setRemoving(null);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [renaming, removing]);
  if (library.length === 0) {
    return (
      <div className="wizard-card" aria-label="开始">
        <h2>还没有测试</h2>
        <p>选一个程序，用一句话说要检查什么，再看它做一遍。</p>
        <ol className="first-steps" aria-label="三步">
          <li><span>1</span>选择程序</li>
          <li><span>2</span>说一句</li>
          <li><span>3</span>看一遍</li>
        </ol>
        <div className="wizard-actions">
          <button type="button" className="btn-accent" onClick={onNew}>新建一个测试</button>
        </div>
      </div>
    );
  }
  const visible = library.filter((item) => {
    const outcome = listedOutcome(item);
    if (filter === "pass") return outcome === "pass";
    if (filter === "update") return outcome === "needs-update";
    if (filter === "fail") return outcome !== "pass";
    return true;
  });
  return (
    <div className="wizard-card" aria-label="已保存的测试">
      <div className="wizard-actions">
        <h2>已保存的测试</h2>
        <button type="button" className="btn-accent" onClick={onNew}>新建一个测试</button>
      </div>
      {library.length > 5 ? (
        <div className="filter-row" role="group" aria-label="筛选">
          {([["all", "全部"], ["fail", "没通过"], ["update", "需要更新"], ["pass", "通过"]] as const).map(([value, label]) => (
            <button key={value} type="button" className={filter === value ? "is-on" : ""} aria-pressed={filter === value} onClick={() => setFilter(value)}>{label}</button>
          ))}
        </div>
      ) : null}
      {missedNote ? <p role="status">{missedNote}</p> : null}
      {visible.length === 0 ? <p>没有要看的测试。</p> : null}
      <ul className="saved-list">
        {visible.map((item) => {
          const outcome = listedOutcome(item);
          const next = item.paused ? "每天重跑已暂停" : `下次 ${formatWhen(nextDailyInstant(item.dailyAt, readClock(), item.weekdays ?? allWeekdays), readClock())}`;
          return (
            <li key={item.id} className={outcome === "fail" ? "is-fail" : outcome === "needs-update" ? "is-update" : ""}>
              <button type="button" className="row-open" onClick={() => onOpen(item)}>
                <strong>{item.name}</strong>
                <span className="result-label">{outcomeWord(outcome)}</span>
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

function AppStep({ picked, connection, recent, installed, busy, onChoose, onCancel, onNext }: {
  picked: TestApp | null;
  connection: ConnectView;
  recent: TestApp[];
  installed: TestApp[];
  busy: boolean;
  onChoose: (app: TestApp | null, options?: { attempt?: number; relaunch?: boolean }) => void;
  onCancel: () => void;
  onNext: () => void;
}) {
  const [confirm, setConfirm] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const connected = connection.state === "connected" && !busy;
  useEffect(() => {
    if (!confirm) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") setConfirm(false);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [confirm]);
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
      {busy ? (
        <>
          <p role="status">正在启动程序。</p>
          <p>连接中。可以取消。</p>
          <button type="button" className="btn" onClick={onCancel}>取消</button>
        </>
      ) : (
        <>
          <p role="status">{connection.status}</p>
          {connection.detail ? <p>{connection.detail}</p> : null}
        </>
      )}
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
        <textarea aria-label="要测什么" value={goal} placeholder="切削工件后，工件变小，旁边的牙不变" onChange={(event) => onGoal(event.target.value)} onKeyDown={(event) => {
          if (event.key === "Enter" && !event.shiftKey) {
            event.preventDefault();
            if (goal.trim().length > 0) onNext();
          }
        }} />
      </label>
      <div className="wizard-actions">
        <button type="button" className="btn" onClick={onBack}>上一步</button>
        <button type="button" className="btn-accent" disabled={goal.trim().length === 0} onClick={onNext}>下一步</button>
      </div>
    </div>
  );
}

const aiSettingsHref = "/settings/ai?return=/plugin/runner";

function DraftStep({ left, keyReady, draft, notice, correction, writing, onCorrection, onRetry, onDemo, onBack, onCancel, onLeave }: {
  left: number; keyReady: boolean; draft: ExecutorCase | null; notice: string; correction: string; writing: boolean;
  onCorrection: (value: string) => void; onRetry: () => void; onDemo: () => void; onBack: () => void; onCancel: () => void; onLeave: () => void;
}) {
  const plan = draft ? plainPlan(draft) : null;
  return (
    <div className="wizard-card" aria-label="看一遍">
      <h2>看它准备做什么</h2>
      <p className="tiny">今天还能生成 {left} 次。{keyReady ? <a href={aiSettingsHref} onClick={onLeave}>去设置</a> : null}</p>
      {writing ? (
        <>
          <p role="status">正在生成检查。</p>
          <p>可以取消。</p>
          <button type="button" className="btn" onClick={onCancel}>取消</button>
        </>
      ) : null}
      {!writing && !keyReady ? <p role="status">{authorMessages.missingKey}<a href={aiSettingsHref} onClick={onLeave}>去设置</a></p> : null}
      {!writing && notice ? <p role="status">{notice}</p> : null}
      {!writing && plan ? (
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
      {!writing && keyReady ? (
        <label>
          哪里不对？
          <textarea aria-label="哪里不对" value={correction} placeholder="例如：旁边的牙不应该变" onChange={(event) => onCorrection(event.target.value)} />
        </label>
      ) : null}
      {!writing ? (
        <div className="wizard-actions">
          <button type="button" className="btn" onClick={onBack}>上一步</button>
          {keyReady ? <button type="button" className="btn" onClick={onRetry}>重新生成</button> : null}
          <button type="button" className="btn-accent" disabled={!draft} onClick={onDemo}>演示一遍</button>
        </div>
      ) : null}
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
        {findings.map((item, index) => {
          const seen = index < shown;
          const word = !seen ? "没跑" : item.kind === "missing" ? "需要更新" : item.ok ? "通过" : "没通过";
          return (
            <li key={item.sentence} data-state={!seen ? "later" : item.kind === "missing" ? "missing" : item.ok ? "ok" : "bad"}>
              <span>{word}</span>
              <span>{item.sentence}</span>
            </li>
          );
        })}
      </ol>
      {shown < findings.length && !stopped ? <p role="status">正在演示。可以取消。</p> : null}
      {stopped ? <p role="status">已取消。可以回去改一句，再看一遍。</p> : null}
      <div className="wizard-actions">
        <button type="button" className="btn" onClick={onBack}>返回修改</button>
        {shown < findings.length && !stopped ? <button type="button" className="btn" onClick={onStop}>取消</button> : null}
      </div>
    </div>
  );
}

const weekdayLabels: Array<[number, string]> = [[1, "周一"], [2, "周二"], [3, "周三"], [4, "周四"], [5, "周五"], [6, "周六"], [7, "周日"]];

function ScheduleStep({ dailyAt, weekdays, autostart, scheduled, onTime, onWeekdays, onAutostart, onSave, onHome }: {
  dailyAt: string; weekdays: number[]; autostart: boolean; scheduled: boolean;
  onTime: (value: string) => void; onWeekdays: (value: number[]) => void; onAutostart: (value: boolean) => void;
  onSave: () => void; onHome: () => void;
}) {
  const toggleDay = (day: number) => {
    const next = weekdays.includes(day) ? weekdays.filter((item) => item !== day) : [...weekdays, day];
    onWeekdays(next.length > 0 ? next : [day]);
  };
  return (
    <div className="wizard-card" aria-label="每天重跑">
      <h2>每天自动再跑一遍</h2>
      <p>到点只重跑这一份，不会再问。到点时这台电脑要开着，工作台也要开着。关着就不会跑。</p>
      <label>
        每天重跑的时间
        <input aria-label="每天重跑的时间" type="time" value={dailyAt} onChange={(event) => onTime(event.target.value)} />
      </label>
      <div className="filter-row" role="group" aria-label="哪些天重跑">
        {weekdayLabels.map(([day, label]) => (
          <button key={day} type="button" className={weekdays.includes(day) ? "is-on" : ""} aria-pressed={weekdays.includes(day)} onClick={() => toggleDay(day)}>{label}</button>
        ))}
      </div>
      <button type="button" className="btn" aria-pressed={autostart} onClick={() => onAutostart(!autostart)}>{autostart ? "开机时自动启动工作台：开" : "开机时自动启动工作台：关"}</button>
      <div className="wizard-actions">
        <button type="button" className="btn" onClick={onHome}>回到首页</button>
        <button type="button" className="btn-accent" onClick={onSave}>保存安排</button>
      </div>
      {scheduled ? <p role="status">已记下。到 {dailyAt} 会再跑一遍，不会再问。</p> : null}
    </div>
  );
}

function ResultPage({ findings, onSave, saveLabel, onAgain, onEdit, note, history, keyReady, canRepair, onRepair, onRerecord, onClearImages }: {
  passed: boolean; findings: Finding[]; onSave: () => void; saveLabel: string; onAgain: () => void; onEdit: () => void; note?: string;
  history: RunRecord[]; keyReady: boolean; canRepair: boolean; onRepair: () => void; onRerecord: () => void; onClearImages: () => void;
}) {
  const [pickedAt, setPickedAt] = useState<string | null>(null);
  const [repairHint, setRepairHint] = useState("");
  const [confirmClear, setConfirmClear] = useState(false);
  useEffect(() => {
    if (!confirmClear) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") setConfirmClear(false);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [confirmClear]);
  const newest = [...history].reverse();
  const picked = newest.find((entry) => entry.at === pickedAt) ?? null;
  const viewingCurrent = !picked || picked.at === newest[0]?.at;
  const outcome = picked?.outcome ?? outcomeOf(findings);
  const sentence = picked && !viewingCurrent
    ? (picked.outcome === "needs-update" ? picked.reason : resultSentence(picked.outcome === "pass"))
    : (outcome === "needs-update" ? findings.find((item) => item.kind === "missing")?.sentence ?? "第 2 步找不到「开始」按钮" : resultSentence(outcome === "pass"));
  const showPicture = (picked ?? newest[0])?.image ?? true;
  const repair = () => {
    if (!keyReady) return;
    if (!canRepair) {
      setRepairHint("今天已经问得够多了。先用保存好的检查，或把每天的次数调大。");
      return;
    }
    onRepair();
  };
  return (
    <div className="wizard-card result-page" aria-label="结果">
      <p className={outcome === "pass" ? "result-mark pass" : outcome === "needs-update" ? "result-mark update" : "result-mark fail"}>{outcomeWord(outcome)}</p>
      <p role="status">{sentence}</p>
      {note && viewingCurrent ? <p>{note}</p> : null}
      {showPicture ? <ResultFrames stuck={outcome === "needs-update"} /> : <p>这一次的画面已经清掉。</p>}
      {viewingCurrent ? (
        <ul className="finding-list" aria-label="核对结果">
          {findings.map((item) => (
            <li key={item.sentence} data-state={item.kind === "missing" ? "missing" : item.ok ? "ok" : "bad"}>
              <strong>{item.kind === "missing" ? "找不到" : item.ok ? "通过" : "没通过"}</strong>
              {item.kind === "missing" ? null : <span>{item.sentence}</span>}
              {item.expected ? <span>{item.expected}</span> : null}
              {item.seen ? <span>{item.seen}</span> : null}
            </li>
          ))}
        </ul>
      ) : null}
      {viewingCurrent ? (
        <details>
          <summary>详情</summary>
          <pre>{findings.map((item) => `${item.kind === "missing" ? "missing" : item.ok ? "ok" : "bad"} ${item.sentence}`).join("\n")}</pre>
        </details>
      ) : null}
      {newest.length > 0 ? (
        <div className="history-block">
          <div className="history-head">
            <h3>以前的结果</h3>
            <button type="button" className="text-btn" onClick={() => setConfirmClear(true)}>清理</button>
          </div>
          <p className="tiny">前后画面只留最近 30 次，免得占太多磁盘。</p>
          <ol className="timeline" aria-label="以前的结果">
            {newest.map((entry) => {
              const current = (picked?.at ?? newest[0]?.at) === entry.at;
              return (
                <li key={entry.at + entry.outcome}>
                  <button type="button" className={current ? "is-current" : ""} aria-current={current ? "true" : undefined} onClick={() => setPickedAt(entry.at)}>
                    <i className={`mark-dot ${entry.outcome === "needs-update" ? "update" : entry.outcome}`} aria-hidden="true" />
                    <span>{formatWhen(new Date(entry.at), readClock())}</span>
                    <span>{outcomeWord(entry.outcome)}</span>
                  </button>
                </li>
              );
            })}
          </ol>
          {confirmClear ? (
            <div role="dialog" aria-label="确认清理">
              <p>清掉保存的前后画面？每次的结果还会留着。</p>
              <div className="wizard-actions">
                <button type="button" className="btn" onClick={() => setConfirmClear(false)}>取消</button>
                <button type="button" className="btn" onClick={() => { setConfirmClear(false); onClearImages(); }}>清理</button>
              </div>
            </div>
          ) : null}
        </div>
      ) : null}
      <div className="wizard-actions">
        {outcome === "needs-update" ? (
          <>
            <button type="button" className="btn" disabled={!keyReady} onClick={repair}>用 AI 修一下</button>
            {!keyReady ? <p className="repair-hint">需要先设置 AI 密钥<a href="/settings/ai?return=/plugin/runner">去设置</a></p> : null}
            {repairHint ? <p className="repair-hint">{repairHint}</p> : null}
            <button type="button" className="btn-accent" onClick={onRerecord}>重新演示并更新</button>
          </>
        ) : null}
        {outcome === "pass" ? <button type="button" className="btn-accent" onClick={onSave}>{saveLabel}</button> : null}
        {outcome === "fail" ? (
          <>
            <button type="button" className="btn" onClick={onEdit}>修改检查</button>
            <button type="button" className="btn-accent" onClick={onAgain}>重新演示</button>
          </>
        ) : null}
      </div>
    </div>
  );
}

function ResultFrames({ stuck }: { stuck: boolean }) {
  if (stuck) {
    return (
      <div className="frames frames-one" aria-label="停住的画面">
        <figure>
          <svg viewBox="0 0 320 200" preserveAspectRatio="xMidYMid slice" role="img" aria-label="停在这里">
            <rect className="frame-bg" width="320" height="200" />
            <rect className="stuck-target" x="70" y="78" width="180" height="44" rx="6" />
          </svg>
          <figcaption>停在这一步。这张画面只留在这台电脑上。</figcaption>
        </figure>
      </div>
    );
  }
  return (
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
  );
}

function Rerecord({ appName, sentence, step, onAdvance, onSave }: {
  appName: string; sentence: string; step: number; onAdvance: () => void; onSave: () => void;
}) {
  const titles = ["重新打开程序", "再做这一步", "保存更新"];
  return (
    <div className="wizard-card" aria-label="重新演示">
      <h2>重新演示并更新</h2>
      <p>{sentence}。不用 AI。</p>
      <ol className="rerecord-steps" aria-label="重新演示的步骤">
        {titles.map((title, index) => (
          <li key={title} aria-current={index === step ? "step" : undefined}>{title}</li>
        ))}
      </ol>
      {step === 0 ? <p role="status">会重新打开「{appName}」。</p> : null}
      {step === 1 ? <p role="status">请在画面上做到刚才找不到的那一步。</p> : null}
      {step === 2 ? <p role="status">这一步已经按现在的画面记下。</p> : null}
      <div className="wizard-actions">
        {step < 2 ? <button type="button" className="btn-accent" onClick={onAdvance}>{step === 0 ? "打开" : "这一步做好了"}</button> : <button type="button" className="btn-accent" onClick={onSave}>保存这次更新</button>}
      </div>
    </div>
  );
}

function listedOutcome(item: SavedTest): "pass" | "fail" | "needs-update" {
  return item.history.at(-1)?.outcome ?? (item.passed ? "pass" : "fail");
}

function readListFilter(): "all" | "fail" | "update" | "pass" {
  try {
    const value = new URLSearchParams(window.location.search).get("filter");
    if (value === "fail" || value === "pass" || value === "update") return value;
  } catch {
    return "all";
  }
  return "all";
}
