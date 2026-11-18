import { useState } from "react";
import { NavLink, useParams } from "react-router-dom";
import type { MarketBadge } from "./catalog";
import type { IconName } from "./catalog";
import { Icon } from "./icons";

export function MarketPage({
  onOpen,
  plugins = [],
}: {
  onOpen: (id: string) => void;
  pinned?: string[];
  onPin?: (id: string, pinned: boolean) => void;
  plugins?: Array<{ id: string; name: string; hint: string; icon: IconName; state?: string; loaded?: boolean }>;
}) {
  return (
    <div className="page">
      <header className="page-head">
        <h1>这台电脑上的插件</h1>
        <p>下面是本机刚读到的插件。没有网上的商店，也不会下载未知插件。</p>
        <MarketNav />
      </header>
      {plugins.length === 0 ? <p>还没有从本机读到可以打开的插件。</p> : (
        <ul className="catalog">
          {plugins.map((item) => (
            <li key={item.id}>
              <span className="catalog-main">
                <Icon name={item.icon} />
                <span>
                  <strong>{item.name}</strong>
                  <small>{item.state ?? item.hint}</small>
                </span>
              </span>
              {item.loaded === false ? <button type="button" className="btn" disabled>没能载入</button> : <button type="button" className="btn" onClick={() => onOpen(item.id)}>打开</button>}
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

export function MarketDetail() {
  const { id } = useParams();
  return (
    <div className="page">
      <p className="crumb">插件 / {id}</p>
      <h1>没有这条网上介绍</h1>
      <p>这里不提供可下载的插件。请回到已安装，看这台电脑上实际读到的插件。</p>
    </div>
  );
}

const installedRows = [
  { id: "upcam", name: "UPCAM 站会日报", summary: "整理今日站会并生成草稿", meta: "星河实验室 · v1.4.2", badge: "官方" as const, icon: "clipboard" as const },
  { id: "ding", name: "钉钉 Insights", summary: "看本周沟通热点与会议节奏", meta: "星河实验室 · v0.9.1", badge: "官方" as const, icon: "chat" as const },
  { id: "test", name: "测试设计", summary: "用例仓库与失败分流", meta: "青裔测试 · v2.1.0", badge: "已签名" as const, icon: "flask" as const },
  { id: "local-pack", name: "本地导入示例包", summary: "用于演示本地清单导入流程", meta: "文前 · v0.1.0", badge: "本地导入" as const, icon: "folder" as const },
];

export function InstalledPage({
  onOpen,
  pinned = [],
  onPin,
  plugins,
}: {
  onOpen: (id: string) => void;
  pinned?: string[];
  onPin?: (id: string, pinned: boolean) => void;
  plugins?: Array<{ id: string; name: string; hint: string; icon: IconName; state?: string; loaded?: boolean }>;
}) {
  const rows = (plugins ?? installedRows.filter((item) => item.id !== "local-pack").map((item) => ({
    id: item.id, name: item.name, hint: item.summary, icon: item.icon, state: "可以用", loaded: true,
  })));
  const [selected, setSelected] = useState(rows[0]?.id ?? "");
  const current = rows.find((item) => item.id === selected) ?? rows[0];
  const held = current ? pinned.includes(current.id) : false;
  return (
    <div className="page split-page">
      <div>
        <header className="page-head">
          <h1>已安装</h1>
          <p>这些插件已经在这台电脑上。固定后出现在左侧，关掉再打开还在。不能从这里卸掉，也不会从网上下载。</p>
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
                  <small>{item.loaded === false ? "没能载入" : pinned.includes(item.id) ? "已固定在左侧" : "没有固定在左侧"}</small>
                </span>
              </button>
              {item.loaded === false ? <button type="button" className="btn" disabled>没能载入</button> : <button type="button" className="btn" onClick={() => onOpen(item.id)}>打开</button>}
            </li>
          ))}
        </ul>
      </div>
      {current ? (
        <aside className="detail-card">
          <h2>{current.name}</h2>
          <p className="sub">{current.state ?? "可以用"} · {held ? "已固定在左侧" : "没有固定在左侧"}</p>
          <p>它随工作台留在这台电脑上。这里不能把它卸掉，也不会去网上找新版本。</p>
          <div className="row-actions">
            <button type="button" className="btn-accent" onClick={() => onOpen(current.id)}>打开</button>
            {onPin && current.loaded !== false ? <button type="button" className="btn" aria-pressed={held} aria-label={held ? `从左侧拿下 ${current.name}` : `把 ${current.name} 固定到左侧`} onClick={() => onPin(current.id, !held)}>{held ? "从左侧拿下" : "固定到左侧"}</button> : null}
          </div>
        </aside>
      ) : <p>没有已安装的插件。</p>}
    </div>
  );
}

export function UpdatesPage() {
  return (
    <div className="page">
      <header className="page-head">
        <h1>更新</h1>
        <p>没有可下载的更新。这些插件随工作台放在这台电脑上，这里不会从网上下载。</p>
        <MarketNav />
      </header>
    </div>
  );
}

export function ImportPage({ onOpen }: { onOpen: (id: string) => void }) {
  const [fileName, setFileName] = useState("本地导入示例包");
  return (
    <div className="page">
      <header className="page-head">
        <h1>本地导入</h1>
        <p>可以先看本机上的文件名。这里不会安装未知插件包，也不会访问网络。</p>
        <MarketNav />
      </header>
      <label className="drop">
        <strong>将插件清单或 .pbpack 拖到这里</strong>
        <span>也可从本机选择文件。导入前会检查签名与所需权限。</span>
        <input aria-label="选择文件" type="file" onChange={(event) => setFileName(event.target.files?.[0]?.name || "本地导入示例包")} />
        <span className="btn">选择文件</span>
      </label>
      <h2>最近导入</h2>
      <div className="import-row">
        <Icon name="folder" />
        <span>
          <strong>{fileName}</strong>
          <small>只显示文件名，还没有安装</small>
        </span>
        <Badge value="本地导入" />
        <button type="button" className="btn" onClick={() => onOpen("local-pack")}>打开</button>
      </div>
      <h2>签名与权限检查</h2>
      <p className="sub">这些是本机策略，不是对刚才那个文件的检查结果。未知插件包不会被安装。</p>
      <ul className="checks">
        <li className="ok">已识别为 .pbpack 插件包</li>
        <li className="warn">未发现发布者签名，导入后将标为「本地导入」</li>
        <li className="ok">申请读取你选择的本机目录</li>
        <li className="ok">申请写入插件数据目录</li>
        <li className="ok">未请求网络访问</li>
      </ul>
    </div>
  );
}

export function Badge({ value }: { value: MarketBadge }) {
  return <span className={`badge badge-${value}`}>{value}</span>;
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
