import { useEffect, useState } from "react";
import "../../shared/panel.css";

export type TestCase = { id: string; title: string; suite: string };
export type TestRun = { id: string; caseId: string; status: string; kind: string; humanAcceptance: string };
export type Dashboard = {
  schema: string;
  schemaVersion: number;
  autoSend: boolean;
  label: string;
  notice: string;
  humanAcceptance: boolean;
  cases: TestCase[];
  runs: TestRun[];
  summary: { passed: number; failed: number; total: number };
};

const schema = "workbench.testing.dashboard.v2.preview";

export function TestingPanel({ load }: { load: () => Promise<Dashboard> }) {
  const [board, setBoard] = useState<Dashboard | null>(null);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");

  const refresh = () => {
    setError("");
    load().then(
      (value) => {
        if (!isBoard(value)) {
          setError("无法读取合成看板。");
          return;
        }
        setBoard(value);
        setStatus("已读取本机合成看板。");
      },
      () => setError("无法读取合成看板。"),
    );
  };

  useEffect(() => {
    let cancelled = false;
    load().then(
      (value) => {
        if (cancelled) return;
        if (isBoard(value)) setBoard(value);
        else setError("无法读取合成看板。");
      },
      () => {
        if (!cancelled) setError("无法读取合成看板。");
      },
    );
    return () => {
      cancelled = true;
    };
  }, [load]);

  return (
    <article className="doc checks-board">
      <header className="doc-head">
        <div>
          <h1>测试设计</h1>
          <p>合成检查看板。不执行测试，不替代人工验收。</p>
        </div>
        <button type="button" className="btn" onClick={refresh}>刷新合成看板</button>
      </header>
      {error ? <p role="alert">{error}</p> : null}
      {!board && !error ? <p>正在读取合成看板…</p> : null}
      {board ? (
        <>
          <p className="plugin-banner" role="note">{board.label}。{board.notice}</p>
          <p>合成检查 {board.summary.passed} / {board.summary.total} 通过，失败 {board.summary.failed}。人工验收：未验收。</p>
          <table className="plugin-table">
            <caption>合成用例</caption>
            <thead><tr><th>编号</th><th>标题</th><th>套件</th></tr></thead>
            <tbody>
              {board.cases.map((item) => (
                <tr key={item.id}><td>{item.id}</td><td>{item.title}</td><td>{item.suite}</td></tr>
              ))}
            </tbody>
          </table>
          <table className="plugin-table">
            <caption>合成运行</caption>
            <thead><tr><th>运行</th><th>用例</th><th>结果</th><th>种类</th><th>人工验收</th></tr></thead>
            <tbody>
              {board.runs.map((run) => (
                <tr key={run.id}><td>{run.id}</td><td>{run.caseId}</td><td>{run.status}</td><td>{run.kind}</td><td>{run.humanAcceptance}</td></tr>
              ))}
            </tbody>
          </table>
        </>
      ) : null}
      {status ? <p role="status">{status}</p> : null}
    </article>
  );
}

function isBoard(value: unknown): value is Dashboard {
  if (!value || typeof value !== "object") return false;
  const document = value as Dashboard;
  return document.schema === schema && document.schemaVersion === 2 && document.autoSend === false
    && document.humanAcceptance === false && document.label.includes("不替代人工验收")
    && Array.isArray(document.cases) && Array.isArray(document.runs) && document.summary?.total === document.runs.length;
}
