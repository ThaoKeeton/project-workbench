import {
  Badge,
  Body1,
  Button,
  Caption1,
  Card,
  FluentProvider,
  Spinner,
  Title2,
  Title3,
  webLightTheme,
  type Theme,
} from "@fluentui/react-components";
import { useCallback, useEffect, useMemo, useState } from "react";
import { NavLink, Route, Routes, useNavigate } from "react-router-dom";
import { TestCasesPanel } from "../../../plugins/v2/Workbench.TestCases.V2/ui/TestCasesPanel";
import { ApiClient, readSessionToken, type Diagnostics, type Info, type PluginItem, type Projects } from "./api";
import { copy, explain, stateLabel } from "./copy";

const theme: Theme = {
  ...webLightTheme,
  fontFamilyBase: '"Segoe UI Variable", "Segoe UI", "Noto Sans CJK SC", "Noto Sans SC", sans-serif',
};

type Snapshot = {
  info: Info;
  projects: Projects;
  plugins: PluginItem[];
  diagnostics: Diagnostics;
};

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

export function App() {
  const token = readSessionToken();
  const client = useMemo(() => new ApiClient(token), [token]);
  const compact = useCompact();
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [error, setError] = useState("");
  const [channel, setChannel] = useState<string>(copy.channelOff);

  const reload = useCallback(async () => {
    const [info, projects, plugins, diagnostics] = await Promise.all([
      client.info(),
      client.projects(),
      client.plugins(),
      client.diagnostics(),
    ]);
    setSnapshot({ info, projects, plugins, diagnostics });
  }, [client]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    let socket: WebSocket | undefined;
    reload().catch((reason: unknown) => {
      if (!cancelled) setError(explain(reason));
    });
    client.openSession().then((opened) => {
      if (!opened || cancelled) return;
      const protocol = window.location.protocol === "https:" ? "wss" : "ws";
      socket = new WebSocket(`${protocol}://${window.location.host}/api/v2/events`);
      socket.addEventListener("open", () => {
        if (!cancelled) setChannel(copy.channelOn);
      });
      socket.addEventListener("message", (event) => {
        const message = JSON.parse(String(event.data)) as { type?: string };
        if (message.type === "project-changed" && !cancelled) {
          reload().catch((reason: unknown) => setError(explain(reason)));
        }
      });
    }).catch((reason: unknown) => {
      if (!cancelled) setError(explain(reason));
    });
    return () => {
      cancelled = true;
      socket?.close();
    };
  }, [client, reload, token]);

  if (!token) {
    return (
      <FluentProvider theme={theme}>
        <p role="alert">{copy.sessionMissing}</p>
      </FluentProvider>
    );
  }

  const active = snapshot?.projects.projects.find((project) => project.id === snapshot.projects.activeProjectId);

  return (
    <FluentProvider theme={theme}>
      <div className="app" data-layout={compact ? "compact" : "wide"}>
        <header className="topbar">
          <div className="brand">
            <Title3>{copy.product}</Title3>
            <Badge appearance="tint" color="brand">{copy.device}</Badge>
            <Caption1>{copy.preview}</Caption1>
          </div>
          <div className="brand">
            <Caption1>{active ? `${copy.currentProject}：${active.name}` : copy.loading}</Caption1>
            <Caption1>{copy.autoSendOff}</Caption1>
            <Caption1>
              {copy.channel}：{channel}
            </Caption1>
          </div>
        </header>
        <nav className="nav" aria-label="主导航">
          <NavLink to="/" end>{copy.nav.home}</NavLink>
          <NavLink to="/projects">{copy.nav.projects}</NavLink>
          <NavLink to="/plugins">{copy.nav.plugins}</NavLink>
          <NavLink to="/diagnostics">{copy.nav.diagnostics}</NavLink>
        </nav>
        <main className="content">
          {error ? <p role="alert">{error}</p> : null}
          {!snapshot && !error ? <Spinner label={copy.loading} /> : null}
          {snapshot ? (
            <Routes>
              <Route path="/" element={<Home snapshot={snapshot} />} />
              <Route path="/projects" element={<Projects client={client} projects={snapshot.projects} onSwitched={setSnapshot} snapshot={snapshot} />} />
              <Route path="/plugins" element={<Plugins plugins={snapshot.plugins} />} />
              <Route path="/plugins/test-cases" element={<Cases client={client} />} />
              <Route path="/diagnostics" element={<DiagnosticsPage diagnostics={snapshot.diagnostics} />} />
            </Routes>
          ) : null}
        </main>
      </div>
    </FluentProvider>
  );
}

function Home({ snapshot }: { snapshot: Snapshot }) {
  const active = snapshot.projects.projects.find((project) => project.id === snapshot.projects.activeProjectId);
  return (
    <section className="stack">
      <Title2 as="h2">{copy.homeTitle}</Title2>
      <Body1>{copy.homeLead}</Body1>
      <div className="grid">
        <Card><CardText title={copy.currentProject} value={active?.name ?? "—"} /></Card>
        <Card><CardText title={copy.pluginCount} value={String(snapshot.diagnostics.plugins.discovered)} /></Card>
        <Card><CardText title={copy.readyPlugins} value={String(snapshot.diagnostics.plugins.ready)} /></Card>
        <Card><CardText title={copy.issueCount} value={String(snapshot.diagnostics.issues.length)} /></Card>
      </div>
      <Caption1>{copy.lanOff}</Caption1>
    </section>
  );
}

function CardText({ title, value }: { title: string; value: string }) {
  return (
    <div className="labeled">
      <Caption1>{title}</Caption1>
      <Title3 as="h3">{value}</Title3>
    </div>
  );
}

function Projects({
  client,
  projects,
  snapshot,
  onSwitched,
}: {
  client: ApiClient;
  projects: Projects;
  snapshot: Snapshot;
  onSwitched: (snapshot: Snapshot) => void;
}) {
  const [message, setMessage] = useState("");
  return (
    <section className="stack">
      <Title2 as="h2">{copy.projectsTitle}</Title2>
      <Body1>{copy.projectsLead}</Body1>
      {projects.projects.map((project) => (
        <Card key={project.id}>
          <div className="project-row">
            <div className="labeled">
              <Title3 as="h3">{project.name}</Title3>
              {project.id === projects.activeProjectId ? <Caption1>{copy.current}</Caption1> : null}
              {project.isArchived ? <Caption1>{copy.archived}</Caption1> : null}
            </div>
            {project.id !== projects.activeProjectId && !project.isArchived ? (
              <Button
                appearance="primary"
                onClick={() => {
                  client.switchProject(project.id, projects.revision).then(
                    async (next) => {
                      const [info, plugins, diagnostics] = await Promise.all([client.info(), client.plugins(), client.diagnostics()]);
                      onSwitched({ info, projects: next, plugins, diagnostics });
                      setMessage(`${copy.currentProject}：${project.name}`);
                    },
                    (reason: unknown) => setMessage(explain(reason)),
                  );
                }}
              >
                {copy.switchTo}
              </Button>
            ) : null}
          </div>
        </Card>
      ))}
      {message ? <p role="status">{message}</p> : null}
      <Caption1>{snapshot.info.device.label}</Caption1>
    </section>
  );
}

function Plugins({ plugins }: { plugins: PluginItem[] }) {
  const navigate = useNavigate();
  return (
    <section className="stack">
      <Title2 as="h2">{copy.pluginsTitle}</Title2>
      <Body1>{copy.pluginsLead}</Body1>
      {plugins.map((plugin) => (
        <Card key={plugin.id}>
          <div className="plugin-card">
            <div className="labeled">
              <Title3 as="h3">{plugin.name}</Title3>
              <Caption1>
                {plugin.contract === "v2-preview" ? copy.contractV2 : copy.contractV1}
                {" · "}
                {stateLabel(plugin.state)}
                {" · "}
                {plugin.loaded ? copy.loaded : copy.notLoaded}
                {" · "}
                {plugin.version}
              </Caption1>
            </div>
            {plugin.uiPath ? (
              <Button appearance="primary" onClick={() => navigate(plugin.uiPath ?? "/plugins")}>{copy.openPlugin}</Button>
            ) : null}
          </div>
        </Card>
      ))}
    </section>
  );
}

function Cases({ client }: { client: ApiClient }) {
  const load = useCallback(() => client.cases(), [client]);
  const save = useCallback((document: Parameters<ApiClient["saveCases"]>[0]) => client.saveCases(document), [client]);
  const exportCsv = useCallback(() => client.exportCases(), [client]);
  return (
    <section className="stack">
      <Title2 as="h2">测试用例 · 示例</Title2>
      <TestCasesPanel
        load={load}
        save={save}
        exportCsv={exportCsv}
        note={copy.casesNote}
        saveLabel={copy.save}
        savedLabel={copy.saved}
        exportLabel={copy.export}
        exportedLabel={copy.exported}
        suiteLabel={copy.suite}
        stepLabel={copy.step}
        expectedLabel={copy.expected}
      />
    </section>
  );
}

function DiagnosticsPage({ diagnostics }: { diagnostics: Diagnostics }) {
  return (
    <section className="stack">
      <Title2 as="h2">{copy.diagnosticsTitle}</Title2>
      <Body1>{copy.diagnosticsLead}</Body1>
      <Card>
        <div className="stack">
          <Body1>{copy.bind}：{diagnostics.service.bind === "loopback" ? copy.bindLoopback : diagnostics.service.bind}</Body1>
          <Body1>{copy.lanOff}</Body1>
          <Body1>{copy.autoSendOff}</Body1>
          <Body1>{copy.nativeWidth}</Body1>
          <Body1>
            {copy.issueCount}：{diagnostics.issues.length === 0 ? "无" : diagnostics.issues.map((issue) => issue.code).join("、")}
          </Body1>
        </div>
      </Card>
    </section>
  );
}
