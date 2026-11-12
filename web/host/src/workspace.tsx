import { useCallback } from "react";
import { NavLink, useNavigate, useParams } from "react-router-dom";
import type { ApiClient } from "./api";
import { pinnedPlugins, type PluginId } from "./catalog";
import { Icon } from "./icons";
import { DingPanel } from "../../../plugins/v2/Workbench.DingTalkInsights.V2/ui/DingPanel";
import { TestCasesPanel } from "../../../plugins/v2/Workbench.TestCases.V2/ui/TestCasesPanel";
import { TestingPanel } from "../../../plugins/v2/Workbench.TestingDesign.V2/ui/TestingPanel";
import { UpcamPanel } from "../../../plugins/v2/Workbench.UpcamStandup.V2/ui/UpcamPanel";
import { ExecutorStudio } from "./executor/studio";

const tabs: PluginId[] = ["upcam", "ding", "test", "runner"];

export function PluginWorkspace({
  client,
  remoteName,
  crashed = false,
  empty = "none",
}: {
  client: ApiClient;
  remoteName?: string;
  crashed?: boolean;
  empty?: "none" | "suite" | "target" | "agent" | "key";
}) {
  const { id = "upcam" } = useParams();
  const plugin = pinnedPlugins.find((item) => item.id === id) ?? pinnedPlugins[0];
  return (
    <div className="workspace">
      <div className="tabs" role="tablist" aria-label="已打开的插件">
        {tabs.map((tab) => {
          const item = pinnedPlugins.find((entry) => entry.id === tab)!;
          return (
            <NavLink key={tab} to={`/plugin/${tab}`} role="tab" className={tab === plugin.id ? "is-on" : ""}>
              <Icon name={item.icon} size={14} />
              {item.name}
            </NavLink>
          );
        })}
        <span className="tab-add" aria-hidden="true">+</span>
      </div>
      {remoteName ? <p className="remote-banner"><i />正在操作 {remoteName} · 延迟 12 ms</p> : null}
      <div className="canvas">
        {crashed ? <CrashState name={plugin.name} /> : null}
        {!crashed && plugin.id === "upcam" ? <UpcamCanvas client={client} /> : null}
        {!crashed && plugin.id === "ding" ? <DingCanvas client={client} /> : null}
        {!crashed && plugin.id === "test" ? <TestingCanvas client={client} /> : null}
        {!crashed && plugin.id === "runner" ? <RunnerCanvas empty={empty} /> : null}
      </div>
    </div>
  );
}

function UpcamCanvas({ client }: { client: ApiClient }) {
  const load = useCallback(() => client.upcamDraft(), [client]);
  const importNotes = useCallback((files: Parameters<ApiClient["upcamImport"]>[0]) => client.upcamImport(files), [client]);
  const loadSample = useCallback(() => client.upcamSample(), [client]);
  return <UpcamPanel load={load} importNotes={importNotes} loadSample={loadSample} />;
}

function DingCanvas({ client }: { client: ApiClient }) {
  const load = useCallback(() => client.dingState(), [client]);
  const importChat = useCallback((text: string) => client.dingImport(text), [client]);
  const loadSample = useCallback(() => client.dingSample(), [client]);
  const analyze = useCallback(() => client.dingAnalyze(), [client]);
  return <DingPanel load={load} importChat={importChat} loadSample={loadSample} analyze={analyze} />;
}

function TestingCanvas({ client }: { client: ApiClient }) {
  const load = useCallback(() => client.testingDashboard(), [client]);
  return <TestingPanel load={load} />;
}

export function SampleCasesPage({ client }: { client: ApiClient }) {
  const load = useCallback(() => client.cases(), [client]);
  const save = useCallback((document: Parameters<ApiClient["saveCases"]>[0]) => client.saveCases(document), [client]);
  const exportCsv = useCallback(() => client.exportCases(), [client]);
  return (
    <article className="doc">
      <h1>测试用例 · 示例</h1>
      <p className="sub">预置示例留在本机。不执行测试，也不发送。</p>
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
  );
}

function RunnerCanvas({ empty }: { empty: "none" | "suite" | "target" | "agent" | "key" }) {
  const navigate = useNavigate();
  if (empty === "target") {
    return (
      <EmptyBlock
        title="找不到目标程序"
        body="套件要操作的「星河任务」现在没有运行，或安装路径已经失效。"
        heading="检测结果"
        rows={[["进程", "Xinghe.Tasks.exe · 未运行"], ["路径", "C:\\Apps\\Xinghe.Tasks.exe · 不存在"], ["上次附着", "11月10日 21:18 · 本机 · 星河笔记本"]]}
        primary="重新检测"
        secondary="选择窗口"
        onPrimary={() => void navigate("/plugin/runner")}
      />
    );
  }
  if (empty === "agent") {
    return (
      <EmptyBlock
        title="Agent 未就绪"
        body="已配对设备上的 Agent 超过 90 秒没有心跳。需要计算机使用的步骤会等到它恢复。"
        heading="状态"
        rows={[["配对", "已配对 · 11月8日 21:16"], ["心跳", "今天 11:14:02 · 已超过 90 秒"], ["Agent", "未响应 · 无待处理任务"], ["远程确认", "尚无待确认操作"]]}
        primary="重新检查"
        secondary="改在本机运行"
        onPrimary={() => void navigate("/plugin/runner")}
      />
    );
  }
  if (empty === "key") {
    return (
      <EmptyBlock
        title="尚未配置模型密钥"
        body="FlaUI 步骤可以立即运行。需要模型判断的步骤会停在「待配置」，不会外发画面。"
        heading="当前能力"
        rows={[["FlaUI 步骤", "可以立即运行"], ["AI 步骤", "会停在「待配置」，不外发画面"], ["密钥", "只写入 Windows 凭据管理器，界面不回显"]]}
        primary="打开运行设置"
        secondary="仅用 FlaUI 继续"
        onPrimary={() => void navigate("/settings")}
      />
    );
  }
  return <ExecutorStudio />;
}

export function EmptyBlock({
  title,
  body,
  heading,
  rows,
  primary,
  secondary,
  onPrimary,
}: {
  title: string;
  body: string;
  heading: string;
  rows: string[][];
  primary: string;
  secondary?: string;
  onPrimary: () => void;
}) {
  return (
    <section className="empty-block">
      <h1>{title}</h1>
      <p>{body}</p>
      <h2>{heading}</h2>
      <ul className="log-rows">
        {rows.map(([label, text]) => (
          <li key={label}><span>{label}</span><span>{text}</span></li>
        ))}
      </ul>
      <div className="row-actions">
        <button type="button" className="btn-accent" onClick={onPrimary}>{primary}</button>
        {secondary ? <button type="button" className="btn">{secondary}</button> : null}
      </div>
    </section>
  );
}

function CrashState({ name }: { name: string }) {
  const navigate = useNavigate();
  return (
    <section className="crash">
      <h1>{name} 已停止运行</h1>
      <p>插件进程意外退出。画布里的未保存编辑可能丢失，你可以重新加载或查看诊断。</p>
      <p className="mono">exit 1 · renderer timeout after 12s</p>
      <div className="row-actions">
        <button type="button" className="btn-accent" onClick={() => void navigate(`/plugin/${name === "测试设计" ? "test" : "upcam"}`)}>重新加载</button>
        <button type="button" className="btn" onClick={() => void navigate("/diagnostics")}>查看诊断</button>
      </div>
      <div className="log-block">
        <h2>最近日志</h2>
        <ul className="log-rows">
          <li><span>11:18:41</span><span>renderer timeout after 12s</span></li>
          <li><span>11:18:40</span><span>canvas flush skipped</span></li>
          <li><span>11:18:29</span><span>last input kept in memory</span></li>
          <li><span>11:18:02</span><span>sandbox was ready</span></li>
        </ul>
      </div>
    </section>
  );
}

export function OfflinePage({ onRetry }: { onRetry: () => void }) {
  const navigate = useNavigate();
  return (
    <section className="offline">
      <h1>本地服务未连接</h1>
      <p>宿主无法联系本机服务。插件、设备发现与市场安装都暂不可用。</p>
      <div className="row-actions">
        <button type="button" className="btn-accent" onClick={onRetry}>重试</button>
        <button type="button" className="btn" onClick={() => void navigate("/diagnostics")}>打开诊断</button>
      </div>
      <div className="log-block">
        <h2>上次已知状态</h2>
        <ul className="log-rows">
          <li><span>心跳</span><span>今天 11:14:02 · 已超过 90 秒</span></li>
          <li><span>沙箱</span><span>已停止 · 无待处理任务</span></li>
          <li><span>发现</span><span>设备发现不可用</span></li>
          <li><span>市场</span><span>安装与更新暂停</span></li>
        </ul>
        <h2>近期日志</h2>
        <ul className="log-rows">
          <li><span>11:14:02</span><span>host.service heartbeat timeout</span></li>
          <li><span>11:14:02</span><span>sandbox stopped</span></li>
          <li><span>11:13:58</span><span>mdns browse closed</span></li>
        </ul>
      </div>
    </section>
  );
}

export function PairingPage() {
  const navigate = useNavigate();
  return (
    <section className="pairing">
      <h1>配对新设备</h1>
      <p>在另一台已安装工作台的设备上输入下方代码</p>
      <p className="pair-code">7K4M — XQ2P</p>
      <p className="sub">代码 10 分钟内有效，仅限同一局域网</p>
      <button type="button" className="btn" onClick={() => void navigate("/")}>返回</button>
      <div className="log-block">
        <h2>配对步骤</h2>
        <ul className="log-rows">
          <li><span>1.</span><span>在另一台设备打开工作台</span></li>
          <li><span>2.</span><span>进入设备页并输入上方代码</span></li>
          <li><span>3.</span><span>确认双方都在同一局域网</span></li>
        </ul>
        <p className="sub">上次成功配对 书房工作站 · 11月8日 21:16</p>
      </div>
    </section>
  );
}

export function DiagnosticsPage({ minimumWidth }: { minimumWidth: number }) {
  return (
    <div className="page narrow-page">
      <header className="page-head">
        <h1>诊断</h1>
        <p>本机服务与插件沙箱状态</p>
      </header>
      <ul className="status-rows">
        <li><span>本机服务</span><strong>仅本机回环 · 已连接</strong></li>
        <li><span>插件沙箱</span><strong>站会、洞察与测试设计均在本机</strong></li>
        <li><span>局域网</span><strong>合成演示，预览不开放局域网</strong></li>
        <li><span>自动发送</span><strong>自动发送：关闭 · 最小宽度 {minimumWidth.toLocaleString("zh-CN")}</strong></li>
      </ul>
      <h2>最近日志</h2>
      <ul className="log-rows">
        <li><span>11:18:02</span><span>站会草稿只写入本机</span></li>
        <li><span>11:17:44</span><span>LAN 发现 书房工作站</span></li>
        <li><span>11:16:09</span><span>市场签名校验通过，4 个更新可用</span></li>
        <li><span>11:12:51</span><span>本地导入示例包已隐藏进度</span></li>
      </ul>
    </div>
  );
}

export function SettingsPage({
  setting,
  onChange,
  restore,
  onRestore,
  reduceMotion,
  onReduceMotion,
}: {
  setting: "system" | "light" | "dark";
  onChange: (setting: "system" | "light" | "dark") => void;
  restore: boolean;
  onRestore: (value: boolean) => void;
  reduceMotion: boolean;
  onReduceMotion: (value: boolean) => void;
}) {
  const options = [
    ["system", "跟随系统", "与操作系统外观保持一致"],
    ["light", "浅色", "始终使用浅色主题"],
    ["dark", "深色", "始终使用深色主题"],
  ] as const;
  return (
    <div className="page narrow-page">
      <header className="page-head">
        <h1>设置</h1>
        <p>外观、通用与关于</p>
      </header>
      <h2>外观</h2>
      <fieldset className="theme-list">
        <legend className="sr">主题</legend>
        {options.map(([value, label, hint]) => (
          <label key={value} className={setting === value ? "is-on" : ""}>
            <input type="radio" name="theme" aria-label={label} value={value} checked={setting === value} onChange={() => onChange(value)} />
            <span><strong>{label}</strong><small>{hint}</small></span>
          </label>
        ))}
      </fieldset>
      <h2>通用</h2>
      <ul className="status-rows">
        <li>
          <span><strong>启动时恢复上次会话</strong><small>打开上次使用的插件</small></span>
          <button type="button" className="text-btn" aria-pressed={restore} onClick={() => onRestore(!restore)}>{restore ? "开启" : "关闭"}</button>
        </li>
        <li>
          <span><strong>界面语言</strong><small>目前仅提供简体中文</small></span>
          <span className="text-btn">简体中文</span>
        </li>
        <li>
          <span><strong>减少动态效果</strong><small>关闭过渡与微动效</small></span>
          <button type="button" className="text-btn" aria-pressed={reduceMotion} onClick={() => onReduceMotion(!reduceMotion)}>{reduceMotion ? "开启" : "关闭"}</button>
        </li>
      </ul>
      <p className="sub">关于 · 版本 0.9.2-preview · 构建 2026.11.11</p>
    </div>
  );
}
