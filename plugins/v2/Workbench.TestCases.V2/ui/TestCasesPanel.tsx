import { useEffect, useState } from "react";
import "./panel.css";

export type CaseRow = { id: string; action: string; expected: string };

export type CaseDocument = {
  schema: string;
  schemaVersion: number;
  suiteName: string;
  rows: CaseRow[];
};

type Props = {
  load: () => Promise<CaseDocument>;
  save: (document: CaseDocument) => Promise<CaseDocument>;
  exportCsv: () => Promise<Blob>;
  note: string;
  saveLabel: string;
  savedLabel: string;
  exportLabel: string;
  exportedLabel: string;
  suiteLabel: string;
  stepLabel: string;
  expectedLabel: string;
};

export function TestCasesPanel(props: Props) {
  const [document, setDocument] = useState<CaseDocument | null>(null);
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;
    props.load().then(
      (value) => {
        if (!cancelled) setDocument(value);
      },
      () => {
        if (!cancelled) setError("无法读取示例用例。");
      },
    );
    return () => {
      cancelled = true;
    };
  }, [props.load]);

  if (error) return <p role="alert">{error}</p>;
  if (!document) return <p>正在读取示例用例…</p>;

  const update = (index: number, field: "action" | "expected", value: string) => {
    setDocument({
      ...document,
      rows: document.rows.map((row, rowIndex) => (rowIndex === index ? { ...row, [field]: value } : row)),
    });
  };

  return (
    <section className="cases" aria-label={document.suiteName}>
      <p>{props.note}</p>
      <p>
        {props.suiteLabel}：{document.suiteName}
      </p>
      {document.rows.map((row, index) => (
        <div className="case-row" key={row.id}>
          <p className="case-id">{row.id}</p>
          <label>
            {row.id} {props.stepLabel}
            <input
              aria-label={`${row.id} ${props.stepLabel}`}
              value={row.action}
              onChange={(event) => update(index, "action", event.target.value)}
            />
          </label>
          <label>
            {row.id} {props.expectedLabel}
            <input
              aria-label={`${row.id} ${props.expectedLabel}`}
              value={row.expected}
              onChange={(event) => update(index, "expected", event.target.value)}
            />
          </label>
        </div>
      ))}
      <div className="case-actions">
        <button
          type="button"
          onClick={() => {
            setError("");
            props.save(document).then(
              (saved) => {
                setDocument(saved);
                setStatus(props.savedLabel);
              },
              () => setError("没有保存。"),
            );
          }}
        >
          {props.saveLabel}
        </button>
        <button
          type="button"
          onClick={() => {
            setError("");
            props.exportCsv().then(
              (blob) => {
                const url = URL.createObjectURL(blob);
                const link = window.document.createElement("a");
                link.href = url;
                link.download = "sample-test-cases.csv";
                link.click();
                URL.revokeObjectURL(url);
                setStatus(props.exportedLabel);
              },
              () => setError("没有导出。"),
            );
          }}
        >
          {props.exportLabel}
        </button>
      </div>
      {status ? <p role="status">{status}</p> : null}
    </section>
  );
}
