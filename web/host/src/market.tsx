import { useMemo, useState } from "react";
import { NavLink, useNavigate, useParams } from "react-router-dom";
import { categories, marketItems, type MarketBadge, type MarketCategory, type MarketItem } from "./catalog";
import type { IconName } from "./catalog";
import { Icon } from "./icons";

const iconFor = (id: string): IconName => {
  if (id === "ding") return "chat";
  if (id === "test" || id === "cases") return "flask";
  if (id === "minutes" || id === "markup") return "chat";
  if (id === "calendar" || id === "backup" || id === "local-pack") return "folder";
  return "clipboard";
};

export function MarketPage({ onOpen, pinned = [], onPin }: { onOpen: (id: string) => void; pinned?: string[]; onPin?: (id: string, pinned: boolean) => void }) {
  const navigate = useNavigate();
  const [query, setQuery] = useState("");
  const [category, setCategory] = useState<(typeof categories)[number]>("全部");
  const visible = useMemo(
    () => marketItems.filter((item) => (category === "全部" || item.category === category) && (!query.trim() || `${item.name}${item.author}${item.summary}`.includes(query.trim()))),
    [category, query],
  );
  const featured = visible.filter((item) => item.featured);
  const list = visible.filter((item) => !item.featured);
  const localAction = (item: MarketItem) => {
    if (!item.pluginId) return <button type="button" className="btn" disabled>不在这台电脑上</button>;
    const held = pinned.includes(item.pluginId);
    return (
      <>
        <button type="button" className="btn" onClick={() => onOpen(item.pluginId!)}>打开</button>
        {onPin ? <button type="button" className="btn" aria-pressed={held} aria-label={held ? `从左侧拿下 ${item.name}` : `把 ${item.name} 固定到左侧`} onClick={() => onPin(item.pluginId!, !held)}>{held ? "从左侧拿下" : "固定到左侧"}</button> : null}
      </>
    );
  };

  return (
    <div className="page">
      <header className="page-head">
        <h1>这台电脑上的插件</h1>
        <p>只使用已经在这台电脑上的插件。没有网上的商店，也不会下载未知插件。</p>
        <MarketNav />
      </header>
      <input className="search-line" aria-label="搜索插件名称、作者或能力" placeholder="搜索插件名称、作者或能力…" value={query} onChange={(event) => setQuery(event.target.value)} />
      <div className="chips" role="tablist" aria-label="分类">
        {categories.map((item) => (
          <button key={item} type="button" role="tab" aria-selected={category === item} className={category === item ? "is-on" : ""} onClick={() => setCategory(item as MarketCategory | "全部")}>{item}</button>
        ))}
      </div>
      {visible.length === 0 ? <p className="empty-inline">找不到目标程序</p> : null}
      <div className="featured">
        {featured.map((item) => (
          <article key={item.id} className="feature-card">
            <Icon name={iconFor(item.id)} size={28} />
            <div>
              <p className="eyebrow">精选</p>
              <h2>{item.name}</h2>
              <p>{item.summary}</p>
            </div>
            {item.pluginId ? <button type="button" className="btn-accent" onClick={() => onOpen(item.pluginId!)}>打开</button> : <button type="button" className="btn" disabled>不在这台电脑上</button>}
          </article>
        ))}
      </div>
      <ul className="catalog">
        {list.map((item) => (
          <li key={item.id}>
            <button type="button" className="catalog-main" onClick={() => { void navigate(`/market/${item.id}`); }}>
              <Icon name={iconFor(item.id)} />
              <span>
                <strong>{item.name}</strong>
                <small>{item.summary}</small>
                <small>{item.author} · {item.version}</small>
              </span>
            </button>
            <Badge value={item.badge} />
            {localAction(item)}
          </li>
        ))}
      </ul>
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
  const item = marketItems.find((entry) => entry.id === id) ?? marketItems[1];
  return (
    <div className="page">
      <p className="crumb">市场 / {item.name}</p>
      <h1>{item.name}</h1>
      <p className="sub">{item.author} · {item.version} · {item.badge}</p>
      <p>从录音稿或站会文字里提取决议、待办与风险项，生成只写入本机的纪要草稿。不会自动发送到任何群聊。</p>
      <h2>界面截图</h2>
      <div className="shots">
        {["纪要草稿", "待办提取", "风险项"].map((label) => <div key={label} className="shot">{label}</div>)}
      </div>
      <h2>安装时将申请这些权限</h2>
      <ul className="plain">
        <li>读取你选择的本机录音稿与文字笔记</li>
        <li>在当前项目写入待办草稿</li>
        <li>读取当前项目名称（星河协作）</li>
      </ul>
      <h2>版本历史</h2>
      <p className="sub">v1.0.3 · 2026年11月3日 · 待办可以按发言人分组</p>
      <h2>适配</h2>
      <p>可在本机与已配对的局域网设备上运行 · 2.4 MB · 2026年11月3日更新</p>
    </div>
  );
}

const installedRows = [
  { id: "upcam", name: "UPCAM 站会日报", summary: "整理今日站会并生成草稿", meta: "星河实验室 · v1.4.2", badge: "官方" as const, icon: "clipboard" as const },
  { id: "ding", name: "钉钉 Insights", summary: "看本周沟通热点与会议节奏", meta: "星河实验室 · v0.9.1", badge: "官方" as const, icon: "chat" as const },
  { id: "test", name: "测试设计", summary: "用例仓库与失败分流", meta: "青裔测试 · v2.1.0", badge: "已签名" as const, icon: "flask" as const },
  { id: "local-pack", name: "本地导入示例包", summary: "用于演示本地清单导入流程", meta: "文前 · v0.1.0", badge: "本地导入" as const, icon: "folder" as const },
];

export function InstalledPage({ onOpen, pinned = [], onPin }: { onOpen: (id: string) => void; pinned?: string[]; onPin?: (id: string, pinned: boolean) => void }) {
  const rows = installedRows.filter((item) => item.id !== "local-pack");
  const [selected, setSelected] = useState(rows[0].id);
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
                  <small>{item.summary}</small>
                  <small>{pinned.includes(item.id) ? "已固定在左侧" : "没有固定在左侧"}</small>
                </span>
              </button>
              <Badge value={item.badge} />
              <button type="button" className="btn" onClick={() => onOpen(item.id)}>打开</button>
            </li>
          ))}
        </ul>
      </div>
      {current ? (
        <aside className="detail-card">
          <h2>{current.name}</h2>
          <p className="sub">{current.meta} · {held ? "已固定在左侧" : "没有固定在左侧"}</p>
          <p>它随工作台留在这台电脑上。这里不能把它卸掉，也不会去网上找新版本。</p>
          <div className="row-actions">
            <button type="button" className="btn-accent" onClick={() => onOpen(current.id)}>打开</button>
            {onPin ? <button type="button" className="btn" aria-pressed={held} aria-label={held ? `从左侧拿下 ${current.name}` : `把 ${current.name} 固定到左侧`} onClick={() => onPin(current.id, !held)}>{held ? "从左侧拿下" : "固定到左侧"}</button> : null}
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
