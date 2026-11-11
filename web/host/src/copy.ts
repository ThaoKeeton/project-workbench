export const copy = {
  product: "项目工作台",
  preview: "v2 预览",
  device: "当前设备：本机",
  nav: { home: "概览", projects: "项目", plugins: "插件", diagnostics: "诊断" },
  autoSendOff: "自动发送：关闭",
  lanOff: "局域网未开放",
  sessionMissing: "未找到本机会话。请从工作台外壳打开。",
  loadFailed: "无法读取本地服务。",
  unauthorized: "会话无效。",
  loading: "正在读取本机服务…",
  homeTitle: "本机概览",
  homeLead: "这是与现有 WinUI 宿主并行的预览界面。数据来自本机服务，自动发送保持关闭。",
  currentProject: "当前项目",
  pluginCount: "已发现插件",
  readyPlugins: "就绪的 v2 插件",
  issueCount: "诊断项",
  channel: "实时通道",
  channelOn: "已连接",
  channelOff: "未连接",
  projectsTitle: "项目",
  projectsLead: "项目目录仍是 schema 1。切换只改变本机预览服务的当前项目。",
  current: "当前项目",
  switchTo: "切换到此项目",
  archived: "已归档",
  pluginsTitle: "插件",
  pluginsLead: "v1 插件只显示发现状态，预览服务不会加载它们。v2 插件嵌在这个窗口里。",
  openPlugin: "打开插件",
  contractV1: "契约 v1",
  contractV2: "契约 v2 预览",
  notLoaded: "未加载",
  loaded: "已加载",
  diagnosticsTitle: "诊断",
  diagnosticsLead: "这里只显示状态码和数量，不显示路径、凭据或机器标识。",
  bind: "监听",
  bindLoopback: "仅本机回环",
  nativeWidth: "本机窗口最小宽度仍为 1,100 像素。浏览器里的界面从 640 像素起排布。",
  casesNote: "这些是预置示例，不会执行测试，也不会发送。",
  save: "保存示例草稿",
  saved: "已保存到本机，未发送。",
  export: "导出本地 CSV",
  exported: "已导出到本机，未发送。",
  suite: "用例集",
  step: "步骤",
  expected: "预期",
} as const;

export function stateLabel(state: string): string {
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

export function explain(error: unknown): string {
  return error instanceof Error && error.message === "Unauthorized" ? copy.unauthorized : copy.loadFailed;
}
