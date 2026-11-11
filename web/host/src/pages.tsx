import { useCallback, useState } from "react";
import { useNavigate } from "react-router-dom";
import { TestCasesPanel } from "../../../plugins/v2/Workbench.TestCases.V2/ui/TestCasesPanel";
import type { ApiClient, PluginItem } from "./api";
import type { Snapshot } from "./App";
import type { ThemeSetting } from "./theme";

const spark = [8, 12, 11, 16, 15, 18, 22];

export function HomePage({ snapshot }: { snapshot: Snapshot }) {
  const project = snapshot.projects.projects.find((item) => item.id === snapshot.projects.activeProjectId);
  const navigate = useNavigate();
  return (
    <section>
      <div className="page-head">
        <div>
          <h1>今天的工作台</h1>
          <div className="sub">{project?.name ?? "当前项目"} · 本机 · 星河笔记本</div>
        </div>
        <div className="actions">
          <button className="btn" type="button" onClick={() => navigate("/devices")}>配对设备</button>
          <button className="btn" type="button" onClick={() => navigate("/plugins")}>查看更新</button>
          <button className="btn-accent" type="button" onClick={() => navigate("/diagnostics")}>打开诊断</button>
        </div>
      </div>
      <div className="metrics">
        <Metric value="36/36" label="合成检查通过" />
        <Metric value="09:20" label="上次合成检查" />
        <Metric value="2" label="设备在线" />
        <Metric value={String(snapshot.diagnostics.issues.length + 1)} label="待处理更新" />
        <article className="card">
          <div className="tiny">近七日检查</div>
          <Sparkline />
          <div className="tiny">合成曲线，不是本机日志</div>
        </article>
      </div>
      <div className="cards">
        <PluginCard title="UPCAM 站会日报" text="今日摘要已就绪。自动发送保持关闭。" meter={72} meterLabel="合成进度 72%" actions={[["发送关闭", false], ["刷新采集", false], ["生成预览", true]]} />
        <PluginCard title="钉钉 Insights" text="上次检查 09:20。默认不发送真实消息。" meter={40} meterLabel="合成进度 40%" actions={[["按需检查", false], ["查看快照", false], ["打开检查", true]]} />
        <PluginCard title="测试设计" text="最近一次合成检查 36/36 通过，不能替代 Windows 人工验收。" meter={100} meterLabel="36/36" actions={[["待人工验收", false], ["运行详情", false], ["查看阶段", true]]} onPrimary={() => navigate("/plugins/test-cases")} />
      </div>
      <div className="split">
        <article className="card">
          <h2>已配对设备</h2>
          <p className="sub">本机、书房工作站在线；会议演示机离线。均为合成数据。</p>
          <div className="row"><span className="status"><span className="dot on" />2 台在线</span><button className="btn" type="button" onClick={() => navigate("/devices")}>管理</button></div>
        </article>
        <article className="card">
          <h2>插件市场</h2>
          <p className="sub">有 1 个更新可检查。安装前需确认信任与权限。</p>
          <div className="row"><span>1 个更新</span><button className="btn" type="button" onClick={() => navigate("/plugins")}>去更新</button></div>
        </article>
        <article className="card">
          <h2>存储与诊断</h2>
          <p className="sub">本机文件锁正常，没有共享冲突。</p>
          <div className="row"><span className="status"><span className="dot on" />正常</span><button className="btn" type="button" onClick={() => navigate("/diagnostics")}>查看</button></div>
        </article>
      </div>
      <article className="card">
        <h2>最近动态</h2>
        <div className="list">
          <div className="line"><span>XH-128 登录页视觉走查</span><span className="tiny">演示席位 · 进行中 · 09:12</span></div>
          <div className="line"><span>XH-141 导出确认流程</span><span className="tiny">演示席位 · 待评审 · 昨天</span></div>
          <div className="line"><span>XH-155 设备配对提示文案</span><span className="tiny">演示席位 · 已完成 · 昨天</span></div>
        </div>
      </article>
      <article className="card" style={{ marginTop: 12 }}>
        <h2>设备一览</h2>
        <div className="list">
          <div className="line"><span className="status"><span className="dot on" />本机 · 星河笔记本</span><span className="status"><span className="dot on" />在线</span><span>当前工作</span></div>
          <div className="line"><span className="status"><span className="dot on" />书房工作站</span><span>在线</span><span className="tiny">合成演示 · 局域网未连接</span></div>
          <div className="line"><span className="status"><span className="dot off" />会议演示机</span><span>离线</span><span className="tiny">合成演示 · 上次 昨天 18:40</span></div>
        </div>
      </article>
      <p className="footer-note">发送保持关闭 · 局域网默认仅本机 · 契约 v2 与 schema 1 并行 · 实时通道：{snapshot.channel}</p>
    </section>
  );
}

export function PluginsPage({ plugins }: { plugins: PluginItem[] }) {
  const navigate = useNavigate();
  return (
    <section className="stack">
      <div className="page-head"><div><h1>插件</h1><div className="sub">v1 插件只显示发现状态。v2 插件嵌在这个窗口里。</div></div></div>
      {plugins.map((plugin) => (
        <article className="card" key={plugin.id}>
          <div className="row">
            <div>
              <h2>{plugin.name}</h2>
              <div className="sub">{plugin.contract === "v2-preview" ? "契约 v2 预览" : "契约 v1"} · {stateLabel(plugin.state)} · {plugin.loaded ? "已加载" : "未加载"} · {plugin.version}</div>
            </div>
            {plugin.uiPath ? <button className="btn-accent" type="button" onClick={() => navigate(plugin.uiPath ?? "/plugins")}>打开插件</button> : null}
          </div>
        </article>
      ))}
    </section>
  );
}

export function ProjectsPage({ client, snapshot, onSwitched }: { client: ApiClient; snapshot: Snapshot; onSwitched: (snapshot: Snapshot) => void }) {
  const [note, setNote] = useState("");
  return (
    <section className="stack">
      <div className="page-head"><div><h1>项目</h1><div className="sub">项目目录仍是 schema 1。切换只改变本机预览服务的当前项目。</div></div></div>
      {snapshot.projects.projects.map((project) => (
        <article className="card" key={project.id}>
          <div className="row">
            <div>
              <h2>{project.name}</h2>
              {project.id === snapshot.projects.activeProjectId ? <div className="sub">当前项目</div> : null}
              {project.isArchived ? <div className="sub">已归档</div> : null}
            </div>
            {project.id !== snapshot.projects.activeProjectId && !project.isArchived ? (
              <button className="btn-accent" type="button" onClick={() => {
                client.switchProject(project.id, snapshot.projects.revision).then(async (next) => {
                  const [info, plugins, diagnostics] = await Promise.all([client.info(), client.plugins(), client.diagnostics()]);
                  onSwitched({ info, projects: next, plugins, diagnostics, channel: snapshot.channel });
                  setNote(`当前项目：${project.name}`);
                }).catch(() => setNote("无法切换项目。"));
              }}>切换到此项目</button>
            ) : null}
          </div>
        </article>
      ))}
      {note ? <p role="status">{note}</p> : null}
    </section>
  );
}

export function DevicesPage() {
  return (
    <section className="stack">
      <div className="page-head"><div><h1>设备</h1><div className="sub">当前设备：本机。列表其余项是合成占位，没有局域网连接。</div></div></div>
      <article className="card"><div className="row"><span className="status"><span className="dot on" />本机 · 星河笔记本</span><span>在线 · 当前工作</span></div></article>
      <article className="card"><div className="row"><span className="status"><span className="dot on" />书房工作站</span><span>合成演示 · 未连接</span></div></article>
      <article className="card"><div className="row"><span className="status"><span className="dot off" />会议演示机</span><span>合成演示 · 离线</span></div></article>
    </section>
  );
}

export function DiagnosticsPage({ snapshot }: { snapshot: Snapshot }) {
  return (
    <section className="stack">
      <div className="page-head"><div><h1>诊断</h1><div className="sub">只显示状态码和数量，不显示路径、凭据或机器标识。</div></div></div>
      <article className="card stack">
        <div>监听：{snapshot.diagnostics.service.bind === "loopback" ? "仅本机回环" : snapshot.diagnostics.service.bind}</div>
        <div>局域网未开放</div>
        <div>自动发送：关闭</div>
        <div>实时通道：{snapshot.channel}</div>
        <div>本机窗口最小宽度仍为 1,100 像素。浏览器里的界面从 640 像素起排布。</div>
        <div>诊断项：{snapshot.diagnostics.issues.length === 0 ? "无" : snapshot.diagnostics.issues.map((issue) => issue.code).join("、")}</div>
      </article>
    </section>
  );
}

export function SettingsPage({ setting, onChange }: { setting: ThemeSetting; onChange: (setting: ThemeSetting) => void }) {
  const theme = document.documentElement.dataset.theme === "light" ? "浅色" : "深色";
  return (
    <section className="stack">
      <div className="page-head">
        <div>
          <h1>设置</h1>
          <div className="sub">主题跟随系统、浅色或深色。当前生效：{theme}。</div>
        </div>
      </div>
      <article className="card">
        <h2>外观</h2>
        <fieldset style={{ border: 0, margin: 0, padding: 0 }}>
          <legend className="sub">主题</legend>
          {(["system", "light", "dark"] as const).map((value) => (
            <label className="choice" key={value}>
              <input type="radio" name="theme" value={value} checked={setting === value} onChange={() => onChange(value)} />
              {value === "system" ? "跟随系统" : value === "light" ? "浅色" : "深色"}
            </label>
          ))}
        </fieldset>
        <p className="tiny">选择保存在本机。嵌在同一窗口里的插件读取 html 上的 data-theme，并使用同一组 CSS 变量。</p>
      </article>
    </section>
  );
}

export function CasesPage({ client }: { client: ApiClient }) {
  const load = useCallback(() => client.cases(), [client]);
  const save = useCallback((document: Parameters<ApiClient["saveCases"]>[0]) => client.saveCases(document), [client]);
  const exportCsv = useCallback(() => client.exportCases(), [client]);
  return (
    <section className="stack">
      <div className="page-head"><div><h1>测试用例 · 示例</h1></div></div>
      <article className="card">
        <TestCasesPanel
          load={load}
          save={save}
          exportCsv={exportCsv}
          note="这些是预置示例，不会执行测试，也不会发送。"
          saveLabel="保存示例草稿"
          savedLabel="已保存到本机，未发送。"
          exportLabel="导出本地 CSV"
          exportedLabel="已导出到本机，未发送。"
          suiteLabel="用例集"
          stepLabel="步骤"
          expectedLabel="预期"
        />
      </article>
    </section>
  );
}

function Metric({ value, label }: { value: string; label: string }) {
  return (
    <article className="card metric">
      <div>
        <strong>{value}</strong>
        <div className="tiny">{label}</div>
      </div>
    </article>
  );
}

function Sparkline() {
  const points = spark.map((value, index) => `${index * 28},${40 - value}`).join(" ");
  return (
    <svg viewBox="0 0 168 44" width="100%" height="44" role="img" aria-label="近七日合成检查曲线">
      <polyline points={points} fill="none" stroke="var(--accent)" strokeWidth="1.5" />
    </svg>
  );
}

function PluginCard({ title, text, meter, meterLabel, actions, onPrimary }: {
  title: string;
  text: string;
  meter: number;
  meterLabel: string;
  actions: Array<[string, boolean]>;
  onPrimary?: () => void;
}) {
  return (
    <article className="card">
      <h2>{title}</h2>
      <p className="sub">{text}</p>
      <div className="meter" role="meter" aria-valuemin={0} aria-valuemax={100} aria-valuenow={meter} aria-label={meterLabel}><span style={{ width: `${meter}%` }} /></div>
      <div className="card-actions">
        {actions.map(([label, primary]) => (
          <button key={label} className={primary ? "btn-accent" : "btn"} type="button" onClick={primary ? onPrimary : undefined}>{label}</button>
        ))}
      </div>
    </article>
  );
}

function stateLabel(state: string): string {
  switch (state) {
    case "Disabled": return "未启用";
    case "Ready": return "就绪";
    case "Invalid": return "无效";
    case "Enabled": return "已启用";
    case "Failed": return "失败";
    case "Stopping": return "正在停止";
    default: return "未知";
  }
}
