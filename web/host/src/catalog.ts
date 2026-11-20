export type IconName =
  | "home"
  | "clipboard"
  | "chat"
  | "flask"
  | "activity"
  | "store"
  | "devices"
  | "diagnostics"
  | "settings"
  | "folder"
  | "laptop"
  | "server";

export type PluginId = "upcam" | "ding" | "test" | "runner";

export const pinnedPlugins: Array<{ id: PluginId; name: string; icon: IconName; hint: string }> = [
  { id: "upcam", name: "UPCAM 站会日报", icon: "clipboard", hint: "整理今日站会并生成草稿" },
  { id: "ding", name: "钉钉 Insights", icon: "chat", hint: "看本周沟通热点与会议节奏" },
  { id: "test", name: "测试设计", icon: "flask", hint: "用例仓库与失败分流" },
  { id: "runner", name: "自动测试", icon: "activity", hint: "选一个程序，用一句话说要测什么" },
];

export const devices = [
  { id: "local", group: "本机", name: "星河笔记本", status: "当前设备", remote: false, icon: "laptop", dot: "on" },
  { id: "study", group: "局域网", name: "书房工作站", status: "在线 · LAN", remote: true, icon: "server", dot: "on" },
  { id: "living", group: "局域网", name: "客厅小主机", status: "休眠", remote: true, icon: "server", dot: "sleep" },
] as const;

export type DeviceId = (typeof devices)[number]["id"];

export function shellPluginId(uiPath: string | null): PluginId | null {
  if (uiPath === "/plugin/upcam") return "upcam";
  if (uiPath === "/plugin/ding") return "ding";
  if (uiPath === "/plugin/test") return "test";
  if (uiPath === "/plugin/runner") return "runner";
  return null;
}

export function pluginStateLabel(loaded: boolean, uiPath: string | null): string {
  if (!uiPath) return "已发现，没有可以打开的页面";
  return loaded ? "可以用" : "没能载入";
}

export function deviceLabel(id: DeviceId): string {
  const device = devices.find((item) => item.id === id);
  if (!device) return "本机 · 星河笔记本";
  return device.remote ? `远程 · ${device.name}` : `本机 · ${device.name}`;
}
