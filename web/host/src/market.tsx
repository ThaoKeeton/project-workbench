import { useState } from "react";
import { Link, NavLink, useParams } from "react-router-dom";
import type { ImportReport } from "./api";
import type { IconName } from "./catalog";
import { Icon } from "./icons";

export type LocalPlugin = {
  id: string;
  openId: string;
  name: string;
  version: string;
  icon: IconName;
  state: string;
  loaded: boolean;
  href: string;
  recorded?: boolean;
};

export function MarketPage({
  onOpen,
  plugins = [],
}: {
  onOpen: (id: string) => void;
  plugins?: LocalPlugin[];
}) {
  return (
    <div className="page">
      <header className="page-head">
        <h1>浏览</h1>
        <p>这里只列出这台电脑上读到的插件。没有网上目录，也不能从这里安装。</p>
        <MarketNav />
      </header>
      {plugins.length === 0 ? <p>这台电脑上还没有读到插件。</p> : (
        <ul className="catalog">
          {plugins.map((item) => (
            <li key={item.id}>
              <Link className="catalog-main" to={`/market/${encodeURIComponent(item.id)}`}>
                <Icon name={item.icon} />
                <span>
                  <strong>{item.name}</strong>
                  <small>本机版本 {item.version}</small>
                  <small>{item.state}</small>
                </span>
              </Link>
              {item.loaded && item.href ? <button type="button" className="btn" onClick={() => onOpen(item.openId)}>打开</button> : <button type="button" className="btn" disabled>{item.state === "已安装，尚未启用" ? "尚未启用" : "不能打开"}</button>}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export function MarketNav() {
  return (
    <nav className="page-links" aria-label="市场视图">
      <NavLink to="/market" end>浏览</NavLink>
      <NavLink to="/market/installed">已安装</NavLink>
      <NavLink to="/market/updates">更新</NavLink>
      <NavLink to="/market/import">本地导入</NavLink>
    </nav>
  );
}

export function MarketDetail({
  plugins = [],
  onOpen,
  onEnable,
  onRemove,
}: {
  plugins?: LocalPlugin[];
  onOpen?: (id: string) => void;
  onEnable?: (id: string) => Promise<void>;
  onRemove?: (id: string) => Promise<void>;
}) {
  const { id = "" } = useParams();
  const plugin = plugins.find((item) => item.id === decodeURIComponent(id));
  if (!plugin) {
    return (
      <div className="page">
        <header className="page-head">
          <h1>这台电脑上没有这个插件</h1>
          <p>没有去网上查，也不能从这里安装。</p>
          <MarketNav />
        </header>
      </div>
    );
  }
  return (
    <div className="page narrow-page">
      <header className="page-head">
        <p className="crumb">浏览 / {plugin.name}</p>
        <h1>{plugin.name}</h1>
        <p>这是这台电脑上读到的记录。没有网上的介绍，也不能从这里安装或卸掉。</p>
        <MarketNav />
      </header>
      <ul className="status-rows">
        <li><span>版本</span><strong>{plugin.version}</strong></li>
        <li><span>状态</span><strong>{plugin.state}</strong></li>
        <li><span>来源</span><strong>这台电脑</strong></li>
      </ul>
      <div className="row-actions">
        {plugin.state === "已安装，尚未启用" && onEnable ? <EnableButton name={plugin.name} onEnable={() => onEnable(plugin.id)} /> : plugin.loaded && plugin.href && onOpen ? <button type="button" className="btn-accent" onClick={() => onOpen(plugin.openId)}>打开</button> : <button type="button" className="btn" disabled>{plugin.state === "已安装，尚未启用" ? "尚未启用" : "不能打开"}</button>}
        {plugin.recorded && onRemove ? <RemoveButton name={plugin.name} onRemove={() => onRemove(plugin.id)} /> : null}
      </div>
    </div>
  );
}

function RemoveButton({ name, onRemove }: { name: string; onRemove: () => Promise<void> }) {
  const [ask, setAsk] = useState(false);
  const [fault, setFault] = useState("");
  return (
    <>
      <button type="button" className="btn" onClick={() => { setFault(""); setAsk(true); }}>卸掉</button>
      {fault ? <p role="status">{fault}</p> : null}
      {ask ? (
        <Confirm
          title={`卸掉「${name}」`}
          body="只会去掉这台电脑上记下的清单。如果它已经启用、正在打开，或固定在左侧、出现在最近打开里，会先拿下来。不会联网，不会下载，也不会删除你当初选中的原文件。"
          confirmLabel="卸掉"
          primaryFirst={false}
          onCancel={() => setAsk(false)}
          onConfirm={() => {
            setAsk(false);
            void onRemove().catch((error: unknown) => {
              setFault(error instanceof Error && error.message ? error.message : "没有卸掉。记下的清单还在。");
            });
          }}
        />
      ) : null}
    </>
  );
}

export function LocalRecordPage({ plugin, onRemove }: { plugin?: LocalPlugin; onRemove?: (id: string) => Promise<void> }) {
  if (!plugin) {
    return (
      <div className="page">
        <h1>这台电脑上没有这个记下的插件</h1>
        <p>记下的清单已经不在，所以没有打开。</p>
      </div>
    );
  }
  if (!plugin.loaded) {
    return (
      <div className="page">
        <h1>还没有启用</h1>
        <p>记下的清单还停在未启用，所以没有打开。</p>
      </div>
    );
  }
  return (
    <div className="page narrow-page">
      <header className="page-head">
        <h1>{plugin.name}</h1>
        <p>已在这台电脑上启用。打开的是记下的清单。没有附带后端程序，也没有从网上下载。</p>
      </header>
      <ul className="status-rows">
        <li><span>版本</span><strong>{plugin.version}</strong></li>
        <li><span>状态</span><strong>可以用</strong></li>
      </ul>
      {onRemove ? <RemoveButton name={plugin.name} onRemove={() => onRemove(plugin.id)} /> : null}
    </div>
  );
}

function EnableButton({ name, onEnable }: { name: string; onEnable: () => Promise<void> }) {
  const [ask, setAsk] = useState(false);
  const [fault, setFault] = useState("");
  return (
    <>
      <button type="button" className="btn-accent" onClick={() => { setFault(""); setAsk(true); }}>启用</button>
      {fault ? <p role="status">{fault}</p> : null}
      {ask ? (
        <Confirm
          title={`启用「${name}」`}
          body="只会加载这台电脑上已经记下的清单。不会联网，也不会下载。"
          confirmLabel="启用"
          primaryFirst={false}
          onCancel={() => setAsk(false)}
          onConfirm={() => {
            setAsk(false);
            void onEnable().catch((error: unknown) => {
              setFault(error instanceof Error && error.message ? error.message : "没有启用。还是停在未启用。");
            });
          }}
        />
      ) : null}
    </>
  );
}

const installedRows = [
  { id: "upcam", name: "UPCAM 站会日报", summary: "整理今日站会并生成草稿", icon: "clipboard" as const },
  { id: "ding", name: "钉钉 Insights", summary: "看本周沟通热点与会议节奏", icon: "chat" as const },
  { id: "test", name: "测试设计", summary: "用例仓库与失败分流", icon: "flask" as const },
];

export function InstalledPage({
  onOpen,
  onEnable,
  onRemove,
  pinned = [],
  onPin,
  plugins,
}: {
  onOpen: (id: string) => void;
  onEnable?: (id: string) => Promise<void>;
  onRemove?: (id: string) => Promise<void>;
  pinned?: string[];
  onPin?: (id: string, pinned: boolean) => void;
  plugins?: Array<{ id: string; name: string; hint: string; icon: IconName; state?: string; loaded?: boolean; recorded?: boolean }>;
}) {
  const rows = (plugins ?? installedRows.map((item) => ({
    id: item.id, name: item.name, hint: item.summary, icon: item.icon, state: "可以用", loaded: true, recorded: false,
  })));
  const [selected, setSelected] = useState(rows[0]?.id ?? "");
  const [askId, setAskId] = useState("");
  const [removeId, setRemoveId] = useState("");
  const [fault, setFault] = useState("");
  const current = rows.find((item) => item.id === selected) ?? rows[0];
  const asked = rows.find((item) => item.id === askId);
  const removing = rows.find((item) => item.id === removeId);
  const held = current ? pinned.includes(current.id) : false;
  return (
    <div className="page split-page">
      <div>
        <header className="page-head">
          <h1>已安装</h1>
          <p>这些插件已经在这台电脑上。固定后出现在左侧，关掉再打开还在。随工作台带来的不能从这里卸掉。本地导入记下的，可以卸掉记下的清单。不会从网上下载。</p>
          <MarketNav />
        </header>
        <ul className="catalog">
          {rows.map((item) => (
            <li key={item.id} className={item.id === current?.id ? "is-current" : ""}>
              <button type="button" className="catalog-main" onClick={() => setSelected(item.id)}>
                <Icon name={item.icon} />
                <span>
                  <strong>{item.name}</strong>
                  <small>{item.state ?? item.hint}</small>
                  <small>{item.state === "已安装，尚未启用" ? "还没有启用" : item.loaded === false ? "没能载入" : pinned.includes(item.id) ? "已固定在左侧" : "没有固定在左侧"}</small>
                </span>
              </button>
              {item.state === "已安装，尚未启用" && onEnable ? <button type="button" className="btn" onClick={() => { setSelected(item.id); setAskId(item.id); }}>启用</button> : item.loaded === false ? <button type="button" className="btn" disabled>{item.state === "已安装，尚未启用" ? "尚未启用" : "没能载入"}</button> : <button type="button" className="btn" onClick={() => onOpen(item.id)}>打开</button>}
              {item.recorded && onRemove ? <button type="button" className="btn" onClick={() => { setSelected(item.id); setRemoveId(item.id); }}>卸掉</button> : null}
            </li>
          ))}
        </ul>
      </div>
      {current ? (
        <aside className="detail-card">
          <h2>{current.name}</h2>
          <p className="sub">{current.state ?? "可以用"} · {held ? "已固定在左侧" : "没有固定在左侧"}</p>
          <p>{current.recorded ? "这是本地导入记下的清单。卸掉只去掉记下的这一份，不会删除你当初选中的原文件，也不会联网。" : "它随工作台留在这台电脑上。这里不能把它卸掉，也不会去网上找新版本。"}</p>
          <div className="row-actions">
            {current.state === "已安装，尚未启用" && onEnable ? <button type="button" className="btn-accent" onClick={() => setAskId(current.id)}>启用</button> : <button type="button" className="btn-accent" disabled={!current.loaded} onClick={() => onOpen(current.id)}>{current.loaded ? "打开" : "不能打开"}</button>}
            {onPin && current.loaded !== false ? <button type="button" className="btn" aria-pressed={held} aria-label={held ? `从左侧拿下 ${current.name}` : `把 ${current.name} 固定到左侧`} onClick={() => onPin(current.id, !held)}>{held ? "从左侧拿下" : "固定到左侧"}</button> : null}
            {current.recorded && onRemove ? <button type="button" className="btn" onClick={() => setRemoveId(current.id)}>卸掉</button> : null}
          </div>
          {fault ? <p role="status">{fault}</p> : null}
        </aside>
      ) : <p>没有已安装的插件。</p>}
      {removing && onRemove ? (
        <Confirm
          title={`卸掉「${removing.name}」`}
          body="只会去掉这台电脑上记下的清单。如果它已经启用、正在打开，或固定在左侧、出现在最近打开里，会先拿下来。不会联网，不会下载，也不会删除你当初选中的原文件。"
          confirmLabel="卸掉"
          primaryFirst={false}
          onCancel={() => setRemoveId("")}
          onConfirm={() => {
            const id = removing.id;
            setRemoveId("");
            void onRemove(id).catch((error: unknown) => {
              setFault(error instanceof Error && error.message ? error.message : "没有卸掉。记下的清单还在。");
            });
          }}
        />
      ) : null}
      {asked && onEnable ? (
        <Confirm
          title={`启用「${asked.name}」`}
          body="只会加载这台电脑上已经记下的清单。不会联网，也不会下载。"
          confirmLabel="启用"
          primaryFirst={false}
          onCancel={() => setAskId("")}
          onConfirm={() => {
            const id = asked.id;
            setAskId("");
            void onEnable(id).catch((error: unknown) => {
              setFault(error instanceof Error && error.message ? error.message : "没有启用。还是停在未启用。");
            });
          }}
        />
      ) : null}
    </div>
  );
}

export function UpdatesPage({ plugins = [] }: { plugins?: LocalPlugin[] }) {
  return (
    <div className="page">
      <header className="page-head">
        <h1>更新</h1>
        <p>没有可下载的更新。下面是这台电脑上的版本，不会从网上下载，也不能在这里卸掉。</p>
        <MarketNav />
      </header>
      {plugins.length === 0 ? <p>这台电脑上还没有读到插件。</p> : (
        <ul className="catalog">
          {plugins.map((item) => (
            <li key={item.id}>
              <span className="catalog-main">
                <Icon name={item.icon} />
                <span>
                  <strong>{item.name}</strong>
                  <small>本机版本 {item.version}</small>
                  <small>没有可下载的更新</small>
                </span>
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export function ImportPage({
  onCheck,
  onCommit,
}: {
  onCheck: (fileName: string, text: string) => Promise<ImportReport>;
  onCommit: (fileName: string, text: string) => Promise<void>;
}) {
  const [fileName, setFileName] = useState("");
  const [text, setText] = useState("");
  const [report, setReport] = useState<ImportReport | null>(null);
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  return (
    <div className="page">
      <header className="page-head">
        <h1>本地导入</h1>
        <p>选择这台电脑上的插件清单。先看对这个文件的检查，确认后才记下。不会启用，也不会联网。</p>
        <MarketNav />
      </header>
      <label className="drop">
        <strong>选择 plugin.v2.json</strong>
        <span>只读取你选中的这个文件。</span>
        <input aria-label="选择文件" type="file" onChange={(event) => {
          const file = event.target.files?.[0];
          setNotice("");
          setReport(null);
          if (!file) return;
          setFileName(file.name);
          void file.text().then(async (body) => {
            setText(body);
            setBusy(true);
            try { setReport(await onCheck(file.name, body)); }
            catch { setNotice("没能检查这个文件。没有写入。"); }
            finally { setBusy(false); }
          });
        }} />
        <span className="btn">选择文件</span>
      </label>
      {fileName ? <p>正在看「{fileName}」。</p> : <p>还没有选择文件。</p>}
      {busy ? <p role="status">正在检查这个文件。</p> : null}
      {notice ? <p role="status">{notice}</p> : null}
      {report ? (
        <>
          <h2>对这个文件的检查</h2>
          <ul className="checks">
            {report.checks.map((check) => <li key={check.id + check.text} className={check.ok ? "ok" : "warn"}>{check.text}</li>)}
          </ul>
          <div className="row-actions">
            <button
              type="button"
              className="btn-accent"
              disabled={!report.importable || busy}
              onClick={() => {
                setBusy(true);
                void onCommit(fileName, text).then(() => {
                  setNotice(report.plugin ? `「${report.plugin.name}」已记在这台电脑上，还没有启用。` : "已记下，还没有启用。");
                }).catch(() => setNotice("没有记下。请看上面的检查。")).finally(() => setBusy(false));
              }}
            >
              确认记在这台电脑上
            </button>
          </div>
        </>
      ) : null}
    </div>
  );
}

export function Confirm({
  title,
  body,
  checks,
  confirmLabel,
  cancelLabel = "取消",
  primaryFirst = true,
  onConfirm,
  onCancel,
}: {
  title: string;
  body: string;
  checks?: string[];
  confirmLabel: string;
  cancelLabel?: string;
  primaryFirst?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const confirm = <button type="button" className="btn-accent" onClick={onConfirm}>{confirmLabel}</button>;
  const cancel = <button type="button" className="btn" onClick={onCancel}>{cancelLabel}</button>;
  return (
    <div className="scrim" role="presentation">
      <div className="dialog" role="dialog" aria-modal="true" aria-labelledby="dialog-title">
        <h2 id="dialog-title">{title}</h2>
        <p>{body}</p>
        {checks ? <ul className="checks">{checks.map((item) => <li key={item} className="ok">{item}</li>)}</ul> : null}
        <div className="row-actions">{primaryFirst ? <>{confirm}{cancel}</> : <>{cancel}{confirm}</>}</div>
      </div>
    </div>
  );
}
