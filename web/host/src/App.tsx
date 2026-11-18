import { useCallback, useEffect, useMemo, useState } from "react";
import { Route, Routes, useLocation, useNavigate, useParams } from "react-router-dom";
import { ApiClient, defaultDesk, readSessionToken, type Desk, type Diagnostics, type Info, type PluginItem, type Projects } from "./api";
import { deviceLabel, devices, pinnedPlugins, pluginStateLabel, shellPluginId, type DeviceId } from "./catalog";
import { CommandPalette, HomePage } from "./launcher";
import { ImportPage, InstalledPage, MarketDetail, MarketPage, UpdatesPage } from "./market";
import { Confirm } from "./market";
import { Shell } from "./shell";
import { applyTheme, readStoredSetting, type ThemeSetting } from "./theme";
import { AiSettingsPage, DiagnosticsPage, OfflinePage, PairingPage, PluginWorkspace, SampleCasesPage, SettingsPage } from "./workspace";

export type Snapshot = {
  info: Info;
  projects: Projects;
  plugins: PluginItem[];
  diagnostics: Diagnostics;
  channel: string;
};

export function App() {
  const token = readSessionToken();
  const client = useMemo(() => new ApiClient(token), [token]);
  const navigate = useNavigate();
  const location = useLocation();
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [error, setError] = useState("");
  const [setting, setSetting] = useState<ThemeSetting>(readStoredSetting());
  const [deviceId, setDeviceId] = useState<DeviceId>("local");
  const [deviceOpen, setDeviceOpen] = useState(false);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [projectOpen, setProjectOpen] = useState(false);
  const [desk, setDesk] = useState<Desk>(defaultDesk);
  const [notice, setNotice] = useState("");
  const [retryDesk, setRetryDesk] = useState<{ pinned: string[]; touch?: string; success: string } | null>(null);
  const [confirm, setConfirm] = useState<{ title: string; body: string; onConfirm: () => void } | null>(null);
  const [restore, setRestore] = useState(true);
  const [reduceMotion, setReduceMotion] = useState(false);

  const reload = useCallback(async () => {
    const [info, projects, plugins, diagnostics, nextDesk] = await Promise.all([
      client.info(),
      client.projects(),
      client.plugins(),
      client.diagnostics(),
      client.desk().catch(() => ({ ...defaultDesk, recovered: true })),
    ]);
    setError("");
    setDesk(nextDesk);
    if (nextDesk.recovered) setNotice("固定和最近打开读不出来，已先按这台电脑上的插件显示。再固定一次会重新记下。");
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
    document.documentElement.dataset.motion = reduceMotion ? "reduce" : "full";
  }, [reduceMotion]);

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

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setPaletteOpen(true);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const chooseTheme = (next: ThemeSetting) => {
    setSetting(next);
    applyTheme(next);
    client.saveTheme(next).catch(() => undefined);
  };

  const saveDesk = (pinned: string[], touch: string | undefined, success: string) => {
    client.saveDesk(pinned, touch).then((saved) => {
      setDesk(saved);
      setRetryDesk(null);
      if (success) setNotice(success);
    }).catch(() => {
      setRetryDesk({ pinned, touch, success });
      setNotice(touch ? "这次打开没能记下来。左侧固定和最近打开还是上次的。" : "没能记下这次改动。左侧还是上次的。");
    });
  };

  const rememberOpen = (id: string) => {
    saveDesk(desk.pinned, id, "");
  };

  const locals = (snapshot?.plugins ?? []).flatMap((plugin) => {
    if (!plugin.uiPath) return [];
    const shellId = shellPluginId(plugin.uiPath);
    const known = shellId ? pinnedPlugins.find((item) => item.id === shellId) : undefined;
    return [{
      id: shellId ?? plugin.id,
      name: plugin.name,
      icon: known?.icon ?? "folder" as const,
      hint: known?.hint ?? plugin.version,
      loaded: plugin.loaded,
      state: pluginStateLabel(plugin.loaded, plugin.uiPath),
      href: plugin.uiPath,
      pinnable: shellId !== null,
    }];
  });
  const shown = locals.length > 0 ? locals : pinnedPlugins.map((plugin) => ({
    ...plugin, loaded: true, state: "可以用", href: `/plugin/${plugin.id}`, pinnable: true,
  }));
  const unopened = (snapshot?.plugins ?? []).filter((plugin) => !plugin.uiPath).map((plugin) => plugin.name);

  const openPlugin = (id: string) => {
    const plugin = shown.find((item) => item.id === id);
    if (!plugin) {
      setNotice("这个插件不在这台电脑上，没有打开。");
      return;
    }
    if (!plugin.loaded) {
      setNotice(`「${plugin.name}」没能载入，没有打开。`);
      return;
    }
    const go = () => {
      if (plugin.pinnable) rememberOpen(plugin.id);
      void navigate(plugin.href);
    };
    const device = devices.find((item) => item.id === deviceId);
    if (device?.remote) {
      setConfirm({
        title: "远程操作确认",
        body: `你即将在「${device.name}」上打开 ${plugin.name}。对方屏幕会看到此次操作，并可随时断开。`,
        onConfirm: go,
      });
      return;
    }
    go();
  };

  const changePin = (id: string, next: boolean) => {
    const pinned = next ? [...desk.pinned.filter((item) => item !== id), id] : desk.pinned.filter((item) => item !== id);
    const plugin = shown.find((item) => item.id === id);
    saveDesk(pinned, undefined, next ? `「${plugin?.name ?? "插件"}」已固定到左侧。关掉再打开还在。` : `「${plugin?.name ?? "插件"}」已从左侧拿下。`);
  };

  const switchProject = (id: string) => {
    const current = snapshot?.projects;
    const revision = current?.revision;
    const currentName = current?.projects.find((project) => project.id === current.activeProjectId)?.name ?? "当前项目";
    const target = current?.projects.find((project) => project.id === id);
    setProjectOpen(false);
    if (!revision || !target) {
      setNotice("没能换过去。项目列表还没读好，请再试一次。");
      return;
    }
    if (id === current?.activeProjectId) return;
    client.switchProject(id, revision).then(async (projects) => {
      const [info, plugins, diagnostics] = await Promise.all([client.info(), client.plugins(), client.diagnostics()]);
      setSnapshot({ info, projects, plugins, diagnostics, channel: snapshot?.channel ?? "已连接" });
      setNotice(`已换到「${target.name}」。`);
    }).catch(async (reason: unknown) => {
      if (reason instanceof Error && reason.message === "RevisionConflict") {
        await reload().catch(() => undefined);
        setNotice("项目列表刚被更新，已重新读取。请再选一次。");
        return;
      }
      setNotice(`没能换到「${target.name}」。现在还是「${currentName}」。`);
    });
  };

  if (!token) return <p role="alert">未找到本机会话。请从工作台外壳打开。</p>;

  const projectName = snapshot?.projects.projects.find((project) => project.id === snapshot.projects.activeProjectId)?.name ?? "星河协作";
  const remote = devices.find((item) => item.id === deviceId)?.remote ?? false;
  const palette = paletteOpen ? (
    <CommandPalette
      projects={(snapshot?.projects.projects ?? [
        { id: "xinghe", name: "星河协作", isArchived: false },
        { id: "lighthouse", name: "灯塔实验", isArchived: false },
      ]).filter((item) => !item.isArchived)}
      activeProjectId={snapshot?.projects.activeProjectId ?? ""}
      onClose={() => setPaletteOpen(false)}
      onOpenPlugin={openPlugin}
      onPickDevice={(id) => { setDeviceId(id); setDeviceOpen(false); }}
      onSwitchProject={switchProject}
      plugins={shown.filter((plugin) => plugin.loaded)}
    />
  ) : null;

  return (
    <Shell
      projectName={projectName}
      activeProjectId={snapshot?.projects.activeProjectId ?? ""}
      projects={(snapshot?.projects.projects ?? []).filter((item) => !item.isArchived)}
      projectOpen={projectOpen}
      onToggleProject={() => { setProjectOpen((open) => !open); setDeviceOpen(false); }}
      onSwitchProject={switchProject}
      deviceId={deviceId}
      deviceOpen={deviceOpen}
      onToggleDevice={() => { setDeviceOpen((open) => !open); setProjectOpen(false); }}
      onPickDevice={(id) => { setDeviceId(id); setDeviceOpen(false); }}
      onPair={() => { setDeviceOpen(false); void navigate("/devices"); }}
      plugins={shown.filter((plugin) => plugin.loaded && desk.pinned.includes(plugin.id))}
      onOpenPlugin={openPlugin}
      notice={notice}
      onRetryNotice={retryDesk ? () => saveDesk(retryDesk.pinned, retryDesk.touch, retryDesk.success) : undefined}
      remote={remote}
    >
      {error && !snapshot ? <OfflinePage onRetry={() => { setError(""); void reload(); }} /> : null}
      {!snapshot && !error ? <p className="page">正在读取本机服务…</p> : null}
      {snapshot ? (
        <Routes>
          <Route path="/" element={<HomePage onOpenPalette={() => setPaletteOpen(true)} palette={palette ?? undefined} plugins={shown} pinned={desk.pinned} recent={desk.recent} onOpen={openPlugin} onPin={changePin} unopened={unopened} />} />
          <Route path="/market" element={<MarketPage onOpen={openPlugin} plugins={shown} />} />
          <Route path="/market/installed" element={<InstalledPage onOpen={openPlugin} pinned={desk.pinned} onPin={changePin} plugins={shown} />} />
          <Route path="/market/updates" element={<UpdatesPage />} />
          <Route path="/market/import" element={<ImportPage onOpen={openPlugin} />} />
          <Route path="/market/:id" element={<MarketDetail />} />
          <Route path="/devices" element={<PairingPage />} />
          <Route path="/diagnostics" element={<DiagnosticsPage minimumWidth={snapshot.diagnostics.nativeWindow.minimumWidth} />} />
          <Route path="/settings" element={<SettingsPage setting={setting} onChange={chooseTheme} restore={restore} onRestore={setRestore} reduceMotion={reduceMotion} onReduceMotion={setReduceMotion} />} />
          <Route path="/settings/ai" element={<AiSettingsPage />} />
          <Route path="/offline" element={<OfflinePage onRetry={() => { void reload(); void navigate("/"); }} />} />
          <Route path="/plugin/:id/stopped" element={<PluginWorkspace client={client} crashed remoteName={remote ? deviceLabel(deviceId).replace("远程 · ", "") : undefined} />} />
          <Route path="/plugin/:id/empty/:scene" element={<PluginEmpty client={client} remoteName={remote ? deviceLabel(deviceId).replace("远程 · ", "") : undefined} />} />
          <Route path="/plugin/:id" element={<PluginWorkspace client={client} remoteName={remote ? deviceLabel(deviceId).replace("远程 · ", "") : undefined} />} />
          <Route path="/plugins/test-cases" element={<SampleCasesPage client={client} />} />
        </Routes>
      ) : null}
      {location.pathname !== "/" ? palette : null}
      {confirm ? (
        <Confirm
          title={confirm.title}
          body={confirm.body}
          confirmLabel="确认并继续"
          primaryFirst={false}
          onCancel={() => setConfirm(null)}
          onConfirm={() => { confirm.onConfirm(); setConfirm(null); }}
        />
      ) : null}
    </Shell>
  );
}

function PluginEmpty({ client, remoteName }: { client: ApiClient; remoteName?: string }) {
  const { scene = "suite" } = useParams();
  const empty = scene === "target" || scene === "agent" || scene === "key" ? scene : "suite";
  return <PluginWorkspace client={client} remoteName={remoteName} empty={empty} />;
}

function messageOf(error: unknown): string {
  return error instanceof Error && error.message === "Unauthorized" ? "会话无效。" : "无法读取本地服务。";
}
