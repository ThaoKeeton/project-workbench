import { useCallback, useEffect, useMemo, useState } from "react";
import { NavLink, Route, Routes, useNavigate } from "react-router-dom";
import { ApiClient, readSessionToken, type Diagnostics, type Info, type PluginItem, type Projects } from "./api";
import { Icon } from "./icons";
import { CasesPage, DevicesPage, DiagnosticsPage, HomePage, PluginsPage, ProjectsPage, SettingsPage } from "./pages";
import { applyTheme, readStoredSetting, type ThemeSetting } from "./theme";

export type Snapshot = {
  info: Info;
  projects: Projects;
  plugins: PluginItem[];
  diagnostics: Diagnostics;
  channel: string;
};

const commands = [
  { label: "打开诊断", path: "/diagnostics" },
  { label: "打开插件", path: "/plugins" },
  { label: "打开设置", path: "/settings" },
  { label: "测试设计", path: "/plugins/test-cases" },
];

export function App() {
  const token = readSessionToken();
  const client = useMemo(() => new ApiClient(token), [token]);
  const compact = useCompact();
  const navigate = useNavigate();
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [error, setError] = useState("");
  const [query, setQuery] = useState("");
  const [menuOpen, setMenuOpen] = useState(false);
  const [setting, setSetting] = useState<ThemeSetting>(readStoredSetting());

  const reload = useCallback(async () => {
    const [info, projects, plugins, diagnostics] = await Promise.all([
      client.info(),
      client.projects(),
      client.plugins(),
      client.diagnostics(),
    ]);
    setSnapshot((current) => ({ info, projects, plugins, diagnostics, channel: current?.channel ?? "未连接" }));
  }, [client]);

  useEffect(() => {
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const apply = () => applyTheme(setting);
    apply();
    media.addEventListener("change", apply);
    return () => media.removeEventListener("change", apply);
  }, [setting]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    let socket: WebSocket | undefined;
    reload().catch((reason: unknown) => {
      if (!cancelled) setError(messageOf(reason));
    });
    client.theme().then((remote) => {
      if (!cancelled && localStorage.getItem("workbench.theme") === null) setSetting(remote);
    }).catch(() => undefined);
    client.openSession().then((opened) => {
      if (!opened || cancelled) return;
      const protocol = window.location.protocol === "https:" ? "wss" : "ws";
      socket = new WebSocket(`${protocol}://${window.location.host}/api/v2/events`);
      socket.addEventListener("open", () => {
        if (!cancelled) setSnapshot((current) => current ? { ...current, channel: "已连接" } : current);
      });
      socket.addEventListener("message", (event) => {
        const message = JSON.parse(String(event.data)) as { type?: string };
        if (message.type === "project-changed" && !cancelled) reload().catch((reason: unknown) => setError(messageOf(reason)));
      });
    }).catch((reason: unknown) => {
      if (!cancelled) setError(messageOf(reason));
    });
    return () => {
      cancelled = true;
      socket?.close();
    };
  }, [client, reload, token]);

  const chooseTheme = (next: ThemeSetting) => {
    setSetting(next);
    applyTheme(next);
    client.saveTheme(next).catch(() => undefined);
  };

  if (!token) return <p role="alert">未找到本机会话。请从工作台外壳打开。</p>;

  const active = snapshot?.projects.projects.find((project) => project.id === snapshot.projects.activeProjectId);
  const matches = query.trim()
    ? commands.filter((item) => item.label.includes(query.trim()))
    : [];

  return (
    <div className="app" data-layout={compact ? "compact" : "wide"}>
      <header className="topbar">
        <div className="wordmark">Project Workbench</div>
        <div className="search">
          <Icon name="search" />
          <input
            aria-label="搜索命令、插件或设备"
            placeholder="搜索命令、插件或设备"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
        </div>
        {matches.length > 0 ? (
          <div className="search-results">
            {matches.map((item) => (
              <button key={item.path} type="button" onClick={() => { setQuery(""); void navigate(item.path); }}>{item.label}</button>
            ))}
          </div>
        ) : null}
        <div className="device">
          <button className="device-pill" type="button" aria-label="当前设备：本机" aria-expanded={menuOpen} onClick={() => setMenuOpen((open) => !open)}>
            <span className="dot on" />
            本机 · 星河笔记本
          </button>
          {menuOpen ? (
            <div className="device-menu" role="menu">
              <div className="device-line"><span>本机 · 星河笔记本</span><span className="status"><span className="dot on" />在线 · 当前工作</span></div>
              <p>其他设备是合成演示，局域网未连接。</p>
            </div>
          ) : null}
        </div>
      </header>
      <nav className="nav" aria-label="主导航">
        <div className="nav-label">当前项目</div>
        <div className="nav-note">{active?.name ?? "正在读取"}</div>
        <NavLink to="/" end><Icon name="home" />首页</NavLink>
        <NavLink to="/plugins"><Icon name="market" />市场</NavLink>
        <NavLink to="/projects"><Icon name="projects" />项目</NavLink>
        <NavLink to="/devices"><Icon name="devices" />设备</NavLink>
        <NavLink to="/diagnostics"><Icon name="diagnostics" />诊断</NavLink>
        <div className="section-label">工作</div>
        <NavLink to="/plugins/test-cases">测试设计</NavLink>
        <NavLink to="/settings"><Icon name="settings" />设置</NavLink>
      </nav>
      <main className="main">
        {error ? <p role="alert">{error}</p> : null}
        {!snapshot && !error ? <p>正在读取本机服务…</p> : null}
        {snapshot ? (
          <Routes>
            <Route path="/" element={<HomePage snapshot={snapshot} />} />
            <Route path="/plugins" element={<PluginsPage plugins={snapshot.plugins} />} />
            <Route path="/plugins/test-cases" element={<CasesPage client={client} />} />
            <Route path="/projects" element={<ProjectsPage client={client} snapshot={snapshot} onSwitched={setSnapshot} />} />
            <Route path="/devices" element={<DevicesPage />} />
            <Route path="/diagnostics" element={<DiagnosticsPage snapshot={snapshot} />} />
            <Route path="/settings" element={<SettingsPage setting={setting} onChange={chooseTheme} />} />
          </Routes>
        ) : null}
      </main>
    </div>
  );
}

function useCompact(): boolean {
  const [compact, setCompact] = useState(() => window.matchMedia("(max-width: 959px)").matches);
  useEffect(() => {
    const media = window.matchMedia("(max-width: 959px)");
    const apply = () => setCompact(media.matches);
    media.addEventListener("change", apply);
    apply();
    return () => media.removeEventListener("change", apply);
  }, []);
  return compact;
}

function messageOf(error: unknown): string {
  return error instanceof Error && error.message === "Unauthorized" ? "会话无效。" : "无法读取本地服务。";
}
