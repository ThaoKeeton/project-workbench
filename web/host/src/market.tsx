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

export function MarketPage({ onOpen }: { onOpen: (id: string) => void }) {
  const navigate = useNavigate();
  const [query, setQuery] = useState("");
  const [category, setCategory] = useState<(typeof categories)[number]>("全部");
  const [installed, setInstalled] = useState<Set<string>>(() => new Set(marketItems.filter((item) => item.installed).map((item) => item.id)));
  const [pending, setPending] = useState<MarketItem | null>(null);
  const visible = useMemo(
    () => marketItems.filter((item) => (category === "全部" || item.category === category) && (!query.trim() || `${item.name}${item.author}${item.summary}`.includes(query.trim()))),
    [category, query],
  );
  const featured = visible.filter((item) => item.featured);
  const list = visible.filter((item) => !item.featured);

  return (
    <div className="page">
      <header className="page-head">
        <h1>插件市场</h1>
        <p>在本机或局域网设备上运行的插件，安装前会说明权限</p>
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
            <button type="button" className="btn-accent" onClick={() => (installed.has(item.id) ? onOpen(item.pluginId ?? item.id) : setPending(item))}>
              {installed.has(item.id) ? "打开" : "安装"}
            </button>
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
            <button type="button" className="btn" onClick={() => (installed.has(item.id) ? onOpen(item.pluginId ?? item.id) : setPending(item))}>
              {installed.has(item.id) ? "打开" : "安装"}
            </button>
          </li>
        ))}
      </ul>
      {pending ? (
        <Confirm
          title={`安装「${pending.name}」`}
          body={`${pending.author}签发 · ${pending.badge}。安装后会出现在左侧图标轨，以及命令条的「插件」分组。演示数据，不是真实权限请求。`}
          checks={pending.id === "minutes"
            ? ["读取你选择的本机录音稿与文字笔记", "在当前项目写入待办草稿", "读取当前项目名称（星河协作）"]
            : ["控制目标窗口（FlaUI）", "截取屏幕，离机前先遮罩", "读取「测试设计」里的用例"]}
          confirmLabel="安装"
          onCancel={() => setPending(null)}
          onConfirm={() => {
            setInstalled((current) => new Set(current).add(pending.id));
            setPending(null);
          }}
        />
      ) : null}
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

export function InstalledPage({ onOpen }: { onOpen: (id: string) => void }) {
  const [selected, setSelected] = useState(installedRows[0].id);
  const [removed, setRemoved] = useState<string[]>([]);
  const rows = installedRows.filter((item) => !removed.includes(item.id));
  const current = rows.find((item) => item.id === selected) ?? rows[0];
  return (
    <div className="page split-page">
      <div>
        <header className="page-head">
          <h1>已安装</h1>
          <p>这些插件可在左侧图标轨里固定或从命令条打开</p>
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
                  <small>{item.meta}</small>
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
          <p className="sub">{current.meta.replace(" · ", " · ")} · {current.badge} · 固定在图标轨</p>
          <h3>权限</h3>
          <ul className="plain">
            <li>读取本机会议录音稿与文字笔记</li>
            <li>在当前项目写入待办草稿</li>
            <li>读取当前项目名称</li>
          </ul>
          <p className="sub">签名 工作台所签发 · 2026年9月12日</p>
          <p className="sub">上次打开 今天 09:42</p>
          <div className="row-actions">
            <button type="button" className="btn-accent" onClick={() => onOpen(current.id)}>打开</button>
            <button type="button" className="btn" onClick={() => setRemoved((items) => [...items, current.id])}>卸载</button>
          </div>
        </aside>
      ) : <p>没有已安装的插件。</p>}
    </div>
  );
}

const updates = [
  {
    id: "upcam",
    name: "UPCAM 站会日报",
    from: "v1.4.2",
    to: "v1.5.0",
    note: "修复摘要截切过长的发言",
    meta: "星河实验室 · v1.5.0",
    badge: "官方" as const,
    icon: "clipboard" as const,
    next: ["待办现在可以按发言人分组", "纪要草稿支持只导出风险项", "修复超长站会录音超时退出"],
    current: ["修正部份日期被识别成金额"],
    currentLabel: "当前版本 v1.4.2 · 10月21日",
  },
  {
    id: "ding",
    name: "钉钉 Insights",
    from: "v0.9.1",
    to: "v0.9.3",
    note: "优化会议热点计算",
    meta: "星河实验室 · v0.9.3",
    badge: "官方" as const,
    icon: "chat" as const,
    next: ["热点计算忽略已撤回的消息", "周报只保留本机草稿"],
    current: ["修复重复的站会提醒"],
    currentLabel: "当前版本 v0.9.1 · 10月18日",
  },
  {
    id: "test",
    name: "测试设计",
    from: "v2.1.0",
    to: "v2.2.0",
    note: "新增失败用例批量分流",
    meta: "青裔测试 · v2.2.0",
    badge: "已签名" as const,
    icon: "flask" as const,
    next: ["失败用例可以按模块批量分流", "导出仍只写本机 CSV"],
    current: ["示例用例保持三行"],
    currentLabel: "当前版本 v2.1.0 · 10月2日",
  },
];

export function UpdatesPage() {
  const [selected, setSelected] = useState(updates[0].id);
  const [done, setDone] = useState<string[]>([]);
  const current = updates.find((item) => item.id === selected) ?? updates[0];
  return (
    <div className="page split-page">
      <div>
        <header className="page-head">
          <h1>更新</h1>
          <p>3 个插件有新版本，其余已是最新</p>
          <MarketNav />
        </header>
        <button type="button" className="btn-accent" onClick={() => setDone(updates.map((item) => item.id))}>更新全部</button>
        <ul className="catalog">
          {updates.map((item) => (
            <li key={item.id} className={item.id === current.id ? "is-current" : ""}>
              <button type="button" className="catalog-main" onClick={() => setSelected(item.id)}>
                <Icon name={item.icon} />
                <span>
                  <strong>{item.name}</strong>
                  <small>{item.from} → {item.to} · {item.note}</small>
                  <small>{item.meta}</small>
                </span>
              </button>
              <Badge value={item.badge} />
              <button type="button" className="btn" onClick={() => setDone((items) => items.includes(item.id) ? items : [...items, item.id])}>{done.includes(item.id) ? "已更新" : "更新"}</button>
            </li>
          ))}
        </ul>
      </div>
      <aside className="detail-card">
        <h2>{current.name}</h2>
        <p className="sub">{current.from} → {current.to}</p>
        <h3>即将安装</h3>
        <ul className="plain">{current.next.map((line) => <li key={line}>{line}</li>)}</ul>
        <h3>{current.currentLabel}</h3>
        <ul className="plain">{current.current.map((line) => <li key={line}>{line}</li>)}</ul>
      </aside>
    </div>
  );
}

export function ImportPage({ onOpen }: { onOpen: (id: string) => void }) {
  const [fileName, setFileName] = useState("本地导入示例包");
  return (
    <div className="page">
      <header className="page-head">
        <h1>本地导入</h1>
        <p>从本机清单或插件包安装，导入的项会标为「本地导入」</p>
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
          <small>今天 10:12 导入 · 未附加发布者签名</small>
          <small>文前 · v0.1.0</small>
        </span>
        <Badge value="本地导入" />
        <button type="button" className="btn" onClick={() => onOpen("local-pack")}>打开</button>
      </div>
      <h2>签名与权限检查</h2>
      <p className="sub">选择文件后会先做这些检查，确认后再写入本地插件目录。</p>
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
