import { useCallback, useEffect, useMemo, useState } from "react";
import { Route, Routes, useLocation, useNavigate, useParams } from "react-router-dom";
import { ApiClient, readSessionToken, type Diagnostics, type Info, type PluginItem, type Projects } from "./api";
import { deviceLabel, devices, pinnedPlugins, type DeviceId } from "./catalog";
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
  const [confirm, setConfirm] = useState<{ title: string; body: string; onConfirm: () => void } | null>(null);
  const [restore, setRestore] = useState(true);
  const [reduceMotion, setReduceMotion] = useState(false);

  const reload = useCallback(async () => {
    const [info, projects, plugins, diagnostics] = await Promise.all([
      client.info(),
      client.projects(),
      client.plugins(),
      client.diagnostics(),
    ]);
    setError("");
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

  const openPlugin = (id: string) => {
    const plugin = pinnedPlugins.find((item) => item.id === id);
    const device = devices.find((item) => item.id === deviceId);
    if (device?.remote) {
      setConfirm({
        title: "远程操作确认",
        body: `你即将在「${device.name}」上打开 ${plugin?.name ?? "插件"}。对方屏幕会看到此次操作，并可随时断开。`,
        onConfirm: () => {
          void navigate(`/plugin/${id}`);
        },
      });
      return;
    }
    void navigate(`/plugin/${id}`);
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
      onSwitchProject={(id) => {
        const revision = snapshot?.projects.revision;
        if (!revision) return;
        client.switchProject(id, revision).then(async (projects) => {
          const [info, plugins, diagnostics] = await Promise.all([client.info(), client.plugins(), client.diagnostics()]);
          setSnapshot({ info, projects, plugins, diagnostics, channel: snapshot?.channel ?? "已连接" });
        }).catch(() => setError("无法切换项目。"));
      }}
    />
  ) : null;

  return (
    <Shell
      projectName={projectName}
      deviceId={deviceId}
      deviceOpen={deviceOpen}
      onToggleDevice={() => setDeviceOpen((open) => !open)}
      onPickDevice={(id) => { setDeviceId(id); setDeviceOpen(false); }}
      onPair={() => { setDeviceOpen(false); void navigate("/devices"); }}
      onOpenPlugin={openPlugin}
      remote={remote}
    >
      {error && !snapshot ? <OfflinePage onRetry={() => { setError(""); void reload(); }} /> : null}
      {!snapshot && !error ? <p className="page">正在读取本机服务…</p> : null}
      {snapshot ? (
        <Routes>
          <Route path="/" element={<HomePage onOpenPalette={() => setPaletteOpen(true)} palette={palette ?? undefined} />} />
          <Route path="/market" element={<MarketPage onOpen={openPlugin} />} />
          <Route path="/market/installed" element={<InstalledPage onOpen={openPlugin} />} />
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
