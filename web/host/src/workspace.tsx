import { useCallback, useState } from "react";
import { NavLink, useLocation, useNavigate, useParams } from "react-router-dom";
import type { ApiClient } from "./api";
import { pinnedPlugins, type PluginId } from "./catalog";
import { Icon } from "./icons";
import { DingPanel } from "../../../plugins/v2/Workbench.DingTalkInsights.V2/ui/DingPanel";
import { TestCasesPanel } from "../../../plugins/v2/Workbench.TestCases.V2/ui/TestCasesPanel";
import { TestingPanel } from "../../../plugins/v2/Workbench.TestingDesign.V2/ui/TestingPanel";
import { UpcamPanel } from "../../../plugins/v2/Workbench.UpcamStandup.V2/ui/UpcamPanel";
import { compatiblePresets, probeAi, readAiSettings, readAuthorFake, readDailyCap, writeAiSettings, writeDailyCap, type AiProvider, type CompatiblePresetId } from "./executor/model";
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
        title="还没连上要测的程序"
        body="请回到向导，重新选择一个可以自己运行的程序。"
        heading="下一步"
        rows={[["现在", "没有连上"], ["可以做的", "重新选择程序"]]}
        primary="重新选择"
        secondary="回到向导"
        onPrimary={() => void navigate("/plugin/runner")}
      />
    );
  }
  if (empty === "agent") {
    return (
      <EmptyBlock
        title="另一台电脑没有回应"
        body="先在这台电脑上测。等那台电脑恢复后，再改到那边。"
        heading="下一步"
        rows={[["现在", "没有回应"], ["可以做的", "改在这台电脑上测"]]}
        primary="改在这台电脑"
        secondary="稍后再试"
        onPrimary={() => void navigate("/plugin/runner")}
      />
    );
  }
  if (empty === "key") {
    return (
      <EmptyBlock
        title="需要先设置 AI 密钥"
        body="没有密钥时不会向外发送。已经保存的检查仍然可以重跑。"
        heading="下一步"
        rows={[["询问", "先去设置"], ["重跑", "不用再问"]]}
        primary="去设置"
        secondary="先用已保存的检查"
        onPrimary={() => void navigate("/settings/ai?return=/plugin/runner")}
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
      <h2>自动测试</h2>
      <p className="sub"><a href="/settings/ai">AI 设置</a></p>
      <label className="setting-cap">
        每天最多生成几次
        <input
          aria-label="每天最多生成几次"
          type="number"
          min={0}
          max={100}
          defaultValue={readDailyCap()}
          onChange={(event) => writeDailyCap(Number(event.target.value))}
        />
      </label>
      <p className="sub">到次数就不再生成。已经保存的测试照常重跑。这里不写金额。</p>
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

export function AiSettingsPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const initial = readAiSettings();
  const [provider, setProvider] = useState<AiProvider>(initial.provider);
  const [secret, setSecret] = useState(initial.key);
  const [base, setBase] = useState(initial.base);
  const [model, setModel] = useState(initial.model);
  const [preset, setPreset] = useState<CompatiblePresetId | "">(initial.preset);
  const [cap, setCap] = useState(initial.cap);
  const [notice, setNotice] = useState("");
  const [savedKey, setSavedKey] = useState(initial.key.length >= 8);
  const [replacing, setReplacing] = useState(false);

  const save = () => {
    writeAiSettings({ provider, key: secret, base, model, preset, cap });
    const kept = readAiSettings().key.length >= 8;
    setSavedKey(kept);
    setReplacing(false);
    const back = new URLSearchParams(location.search).get("return");
    if (back && back.startsWith("/") && !back.startsWith("//") && !back.includes("\\")) {
      window.sessionStorage.setItem("workbench.wizard.resume", "1");
      void navigate(back);
      return;
    }
    setNotice(kept ? "已保存" : "");
  };

  const choosePreset = (id: CompatiblePresetId) => {
    const next = compatiblePresets.find((item) => item.id === id);
    if (!next) return;
    setPreset(id);
    setBase(next.base);
    setModel(next.model);
  };

  return (
    <div className="page narrow-page ai-settings">
      <header className="page-head">
        <h1>AI 设置</h1>
        <p>密钥交给这台电脑的系统凭据保存，页面上会遮住，也不会写进浏览器。每天重跑不会再用 AI。</p>
      </header>
      <fieldset className="theme-list">
        <legend>提供方</legend>
        <label className={provider === "openai" ? "is-on" : ""}>
          <input type="radio" name="provider" aria-label="OpenAI" value="openai" checked={provider === "openai"} onChange={() => setProvider("openai")} />
          <span><strong>OpenAI</strong><small>用这家的接口生成检查</small></span>
        </label>
        <label className={provider === "compatible" ? "is-on" : ""}>
          <input type="radio" name="provider" aria-label="兼容接口" value="compatible" checked={provider === "compatible"} onChange={() => setProvider("compatible")} />
          <span><strong>兼容接口</strong><small>选一个常用的，或自己填地址</small></span>
        </label>
      </fieldset>
      {provider === "compatible" ? (
        <>
          <div className="ai-presets" role="group" aria-label="常用接口">
            {compatiblePresets.map((item) => (
              <button key={item.id} type="button" className={preset === item.id ? "is-on" : ""} aria-pressed={preset === item.id} onClick={() => choosePreset(item.id)}>{item.label}</button>
            ))}
          </div>
          <label className="setting-cap">
            接口地址
            <input aria-label="接口地址" value={base} onChange={(event) => { setBase(event.target.value); setPreset("custom"); }} />
          </label>
          <label className="setting-cap">
            模型
            <input aria-label="模型" value={model} onChange={(event) => { setModel(event.target.value); setPreset("custom"); }} />
          </label>
        </>
      ) : null}
      {savedKey && !replacing ? (
        <div className="ai-saved">
          <strong>已保存</strong>
          <span aria-label="已保存的密钥">••••••••</span>
          <button type="button" className="btn" onClick={() => { setReplacing(true); setSecret(""); setNotice(""); }}>更换</button>
          <button type="button" className="btn" onClick={() => { setSecret(""); setSavedKey(false); setReplacing(false); setNotice(""); writeAiSettings({ provider, key: "", base, model, preset, cap }); }}>删除</button>
        </div>
      ) : (
        <label className="setting-cap">
          密钥
          <input aria-label="密钥" type="password" autoComplete="off" value={secret} onChange={(event) => setSecret(event.target.value)} />
        </label>
      )}
      <label className="setting-cap">
        每天最多生成几次
        <input aria-label="每天最多生成几次" type="number" min={0} max={100} value={cap} onChange={(event) => setCap(Number(event.target.value))} />
      </label>
      <p className="sub">到次数就不再生成。已经保存的测试照常重跑。这里不写金额。每天重跑不会再用 AI。</p>
      {notice ? <p role="status">{notice}</p> : null}
      <p className="ai-actions">
        <button type="button" className="btn" onClick={() => void probeAi({ key: savedKey && !replacing ? readAiSettings().key : secret, fake: readAuthorFake() }).then((result) => setNotice(result.message))}>测试一下</button>
        <button type="button" className="btn-accent" onClick={save}>保存</button>
      </p>
    </div>
  );
}
