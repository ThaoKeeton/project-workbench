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

export type MarketBadge = "官方" | "已签名" | "本地导入";
export type MarketCategory = "效率" | "协作" | "测试" | "工具";

export type MarketItem = {
  id: string;
  pluginId?: PluginId;
  name: string;
  summary: string;
  author: string;
  version: string;
  badge: MarketBadge;
  category: MarketCategory;
  installed: boolean;
  featured?: boolean;
  blurb?: string;
};

export const pinnedPlugins: Array<{ id: PluginId; name: string; icon: IconName; hint: string }> = [
  { id: "upcam", name: "UPCAM 站会日报", icon: "clipboard", hint: "整理今日站会并生成草稿" },
  { id: "ding", name: "钉钉 Insights", icon: "chat", hint: "看本周沟通热点与会议节奏" },
  { id: "test", name: "测试设计", icon: "flask", hint: "用例仓库与失败分流" },
  { id: "runner", name: "自动测试", icon: "activity", hint: "选一个程序，用一句话说要测什么" },
];

export const todayItems = [
  { time: "09:20", source: "钉钉 Insights", title: "合成导出可按需分析，发送保持关闭" },
  { time: "09:12", source: "UPCAM", title: "SYN-014 登录页对照仍在进行" },
  { time: "昨天", source: "测试设计", title: "合成检查 6 / 6 通过，不替代人工验收" },
];

export const recentPlugins = [
  { id: "upcam" as const, name: "UPCAM 站会日报", hint: "采集站会记录并生成今日摘要", icon: "clipboard" as const },
  { id: "ding" as const, name: "钉钉 Insights", hint: "按需检查群聊洞察，不代发", icon: "chat" as const },
  { id: "test" as const, name: "测试设计", hint: "运行合成检查看板，仍需人工验收", icon: "flask" as const },
  { id: "runner" as const, name: "自动测试", hint: "选程序，说一句，看着它做一遍", icon: "activity" as const },
];

export const devices = [
  { id: "local", group: "本机", name: "星河笔记本", status: "当前设备", remote: false, icon: "laptop", dot: "on" },
  { id: "study", group: "局域网", name: "书房工作站", status: "在线 · LAN", remote: true, icon: "server", dot: "on" },
  { id: "living", group: "局域网", name: "客厅小主机", status: "休眠", remote: true, icon: "server", dot: "sleep" },
] as const;

export type DeviceId = (typeof devices)[number]["id"];

export const marketItems: MarketItem[] = [
  {
    id: "upcam",
    pluginId: "upcam",
    name: "UPCAM 站会日报",
    summary: "整理今日站会并生成只留在本机的日报草稿",
    blurb: "整理今日站会并生成草稿",
    author: "星河实验室",
    version: "v1.4.2",
    badge: "官方",
    category: "效率",
    installed: true,
    featured: true,
  },
  {
    id: "minutes",
    name: "会议纪要助手",
    summary: "从录音稿提取决议与待办，不自动外发",
    author: "青裔测试",
    version: "v1.0.3",
    badge: "已签名",
    category: "协作",
    installed: false,
    featured: true,
  },
  {
    id: "ding",
    pluginId: "ding",
    name: "钉钉 Insights",
    summary: "看本周沟通热点与会议节奏",
    author: "星河实验室",
    version: "v0.9.1",
    badge: "官方",
    category: "协作",
    installed: true,
  },
  {
    id: "test",
    pluginId: "test",
    name: "测试设计",
    summary: "用例仓库与失败分流",
    author: "青裔测试",
    version: "v2.1.0",
    badge: "已签名",
    category: "测试",
    installed: true,
  },
  {
    id: "calendar",
    name: "日历同步",
    summary: "把日程与站会纪要对齐到本周视图",
    author: "林间工作室",
    version: "v1.2.0",
    badge: "已签名",
    category: "效率",
    installed: false,
  },
  {
    id: "backup",
    name: "本地备份助手",
    summary: "将插件数据定期写入本机目录",
    author: "北窗",
    version: "v0.8.4",
    badge: "已签名",
    category: "工具",
    installed: false,
  },
  {
    id: "markup",
    name: "截图批注",
    summary: "在截图上圈点并回写到事项",
    author: "北窗",
    version: "v1.1.1",
    badge: "已签名",
    category: "工具",
    installed: false,
  },
  {
    id: "cases",
    name: "用例仓库",
    summary: "把测试用例按模块归档检索",
    author: "青裔测试",
    version: "v3.0.0",
    badge: "官方",
    category: "测试",
    installed: false,
  },
];

export const categories = ["全部", "效率", "协作", "测试", "工具"] as const;

export function deviceLabel(id: DeviceId): string {
  const device = devices.find((item) => item.id === id);
  if (!device) return "本机 · 星河笔记本";
  return device.remote ? `远程 · ${device.name}` : `本机 · ${device.name}`;
}
