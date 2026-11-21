import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "../../../plugins/v2");
const standupSample = readFileSync(join(root, "Workbench.UpcamStandup.V2/Samples/today.txt"), "utf8");
const chatSample = readFileSync(join(root, "Workbench.DingTalkInsights.V2/Samples/chat.txt"), "utf8");
const dashboardSample = JSON.parse(readFileSync(join(root, "Workbench.TestingDesign.V2/Samples/dashboard.json"), "utf8"));
const statuses = new Set(["进行中", "待确认", "已完成", "待复核"]);
const topics = [
  ["topic-login-review", "登录页对照"],
  ["topic-export", "导出确认"],
  ["topic-synthetic-check", "合成检查"],
];

export function shanghaiToday(now = new Date()) {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Shanghai", year: "numeric", month: "2-digit", day: "2-digit" }).format(now);
}

export function emptyDigest(today = shanghaiToday()) {
  return {
    schema: "workbench.upcam.digest.v2.preview", schemaVersion: 2, autoSend: false, delivered: false, stored: false, source: "none",
    meetingDate: today, summary: "还没有今日草稿。", speechSummary: "", items: [], speeches: [], skipped: [], issues: [],
  };
}

export function emptyInsights() {
  return {
    schema: "workbench.dingtalk.insights.v2.preview", schemaVersion: 2, autoSend: false, sendEnabled: false, webhookConfigured: false,
    delivered: false, stored: false, source: "none", imported: false, analyzed: false, conversation: "", messageCount: 0, insights: [],
  };
}

export function upcamSample(today = shanghaiToday()) {
  return buildDigest([{ name: "synthetic-standup.txt", text: standupSample.replaceAll("{{today}}", today) }], today, "synthetic-sample");
}

export function upcamImport(body, today = shanghaiToday()) {
  const files = body?.files;
  if (!Array.isArray(files) || files.length === 0 || files.length > 32) return { status: 400, body: { error: "InvalidPluginData" } };
  if (files.some((file) => typeof file?.text !== "string" || file.text.length === 0 || file.text.length > 65536)) return { status: 400, body: { error: "InvalidPluginData" } };
  return { status: 200, body: buildDigest(files, today, "local-file") };
}

export function dingSample() {
  return { status: 200, chat: parseChat(chatSample), source: "synthetic-sample" };
}

export function dingImport(body) {
  if (typeof body?.text !== "string" || body.text.length === 0 || body.text.length > 65536) return { status: 400, body: { error: "InvalidPluginData" } };
  try {
    return { status: 200, chat: parseChat(body.text), source: "local-file" };
  } catch {
    return { status: 400, body: { error: "InvalidPluginData" } };
  }
}

export function analyzeChat(chat, source) {
  return {
    ...emptyInsights(),
    stored: true,
    source,
    imported: true,
    analyzed: true,
    conversation: chat.conversation,
    messageCount: chat.messages.length,
    insights: insightsFor(chat),
  };
}

export function insightState(chat, source) {
  return {
    ...emptyInsights(),
    stored: true,
    source,
    imported: true,
    conversation: chat.conversation,
    messageCount: chat.messages.length,
  };
}

export function sendDisabled() {
  return { error: "SendDisabled", autoSend: false, sendEnabled: false, webhookConfigured: false, delivered: false };
}

export function dashboardDocument() {
  const runs = dashboardSample.runs;
  return {
    ...dashboardSample,
    summary: {
      passed: runs.filter((run) => run.status === "通过").length,
      failed: runs.filter((run) => run.status === "未通过").length,
      total: runs.length,
    },
  };
}

function buildDigest(files, today, source) {
  const items = [];
  const skipped = [];
  const issues = [];
  for (const file of files) {
    const parsed = parseStandup(file.name, file.text, today);
    issues.push(...parsed.issues);
    if (parsed.skipped) {
      skipped.push({ name: parsed.name, reason: parsed.reason });
      continue;
    }
    for (const item of parsed.items) {
      if (items.some((existing) => existing.id === item.id)) {
        issues.push("有一条重复的事项编号");
        continue;
      }
      items.push(item);
    }
  }
  if (items.length === 0) issues.push("今日没有可写入草稿的事项");
  const counts = (status) => items.filter((item) => item.status === status).length;
  const summary = items.length === 0
    ? "今日没有可写入草稿的事项。自动发送已关闭，没有外发。"
    : `今日 ${today} 共 ${items.length} 项。进行中 ${counts("进行中")}，待确认 ${counts("待确认")}，已完成 ${counts("已完成")}，待复核 ${counts("待复核")}。自动发送已关闭，草稿只留在本机。`;
  const speeches = items.filter((item) => item.speech).map((item) => ({ owner: item.owner, text: item.speech }));
  return {
    schema: "workbench.upcam.digest.v2.preview",
    schemaVersion: 2,
    autoSend: false,
    delivered: false,
    stored: true,
    source,
    meetingDate: today,
    summary,
    speechSummary: speeches.length === 0 ? "今日事项没有发言摘录。" : speeches.map((item) => `${item.owner}：${item.text}`).join("\n"),
    items: items.map(({ id, title, owner, status }) => ({ id, title, owner, status })),
    speeches,
    skipped,
    issues,
  };
}

function parseStandup(name, text, today) {
  const label = baseName(name);
  let date = null;
  const items = [];
  const issues = [];
  let current = null;
  for (const raw of String(text).replaceAll("\r\n", "\n").replaceAll("\r", "\n").split("\n")) {
    const line = raw.trim();
    if (line.length === 0 || line.startsWith("#")) continue;
    if (line.startsWith("日期:")) {
      const value = line.slice("日期:".length).trim();
      date = /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : null;
      continue;
    }
    if (line.startsWith("事项:")) {
      if (current) finish(current, items, issues);
      current = { id: line.slice("事项:".length).trim(), title: "", owner: "", status: "", speech: "" };
      continue;
    }
    if (!current) continue;
    if (line.startsWith("标题:")) current.title = line.slice("标题:".length).trim();
    else if (line.startsWith("负责人:")) current.owner = line.slice("负责人:".length).trim();
    else if (line.startsWith("状态:")) current.status = line.slice("状态:".length).trim();
    else if (line.startsWith("发言:")) current.speech = line.slice("发言:".length).trim();
  }
  if (current) finish(current, items, issues);
  if (!date) return { name: label, skipped: true, reason: "UnreadableDate", items: [], issues };
  if (date !== today) return { name: label, skipped: true, reason: "NotToday", items: [], issues };
  return { name: label, skipped: false, reason: null, items, issues };
}

function finish(item, items, issues) {
  if (!/^SYN-\d{3,6}$/.test(item.id) || items.some((existing) => existing.id === item.id)) {
    issues.push(items.some((existing) => existing.id === item.id) ? "有一条重复的事项编号" : "有一条事项编号无法识别");
    return;
  }
  if (!valid(item.title, 80)) {
    issues.push("有一条事项缺少标题");
    return;
  }
  if (!valid(item.owner, 20)) {
    item.owner = "待复核";
    issues.push("有一条事项的负责人已改为待复核");
  }
  if (!statuses.has(item.status)) {
    item.status = "待复核";
    issues.push("有一条事项的状态已改为待复核");
  }
  if (item.speech && !valid(item.speech, 500)) {
    issues.push("有一条发言无法收录");
    item.speech = "";
  }
  items.push(item);
}

function parseChat(text) {
  const body = String(text).trim().replace(/^\uFEFF/, "");
  if (body.startsWith("{")) return parseChatJson(body);
  let conversation = "";
  const messages = [];
  for (const raw of body.replaceAll("\r\n", "\n").replaceAll("\r", "\n").split("\n")) {
    const line = raw.trim();
    if (line.length === 0 || line.startsWith("#")) continue;
    if (line.startsWith("会话:")) {
      conversation = line.slice("会话:".length).trim();
      continue;
    }
    const parts = line.split(" | ");
    if (parts.length < 4) throw new Error("invalid");
    const [id, at, speaker, ...rest] = parts;
    messages.push({ id: id.trim(), at: at.trim(), speaker: speaker.trim(), text: rest.join(" | ").trim() });
  }
  return finishChat(conversation, messages);
}

function parseChatJson(body) {
  const document = JSON.parse(body);
  if (document.schema !== "workbench.dingtalk.export.v2.preview" || document.schemaVersion !== 2) throw new Error("invalid");
  if (!Array.isArray(document.messages) || document.messages.length === 0 || document.messages.length > 200) throw new Error("invalid");
  return finishChat(document.conversation, document.messages);
}

function finishChat(conversation, rows) {
  if (!valid(conversation, 40)) throw new Error("invalid");
  const messages = [];
  for (const row of rows) {
    if (!/^M-\d{3,6}$/.test(row.id) || messages.some((existing) => existing.id === row.id)) throw new Error("invalid");
    if (typeof row.at !== "string" || (!row.at.includes("+") && !row.at.endsWith("Z"))) throw new Error("invalid");
    if (Number.isNaN(Date.parse(row.at))) throw new Error("invalid");
    if (!valid(row.speaker, 20) || !valid(row.text, 400)) throw new Error("invalid");
    messages.push({ id: row.id, at: row.at, speaker: row.speaker, text: row.text });
  }
  return { conversation, messages };
}

function insightsFor(chat) {
  const insights = [];
  for (const [id, phrase] of topics) {
    const hits = chat.messages.filter((message) => message.text.includes(phrase));
    if (hits.length === 0) continue;
    insights.push({ id, kind: "topic", title: `${phrase}出现 ${hits.length} 次`, detail: "只按原文计数。", evidence: evidence(hits) });
  }
  const hours = new Map();
  for (const message of chat.messages) {
    const hour = new Intl.DateTimeFormat("en-GB", { timeZone: "Asia/Shanghai", hour: "2-digit", hourCycle: "h23" }).format(new Date(message.at));
    const group = hours.get(hour) ?? [];
    group.push(message);
    hours.set(hour, group);
  }
  const busiest = [...hours.entries()].sort((left, right) => right[1].length - left[1].length || left[0].localeCompare(right[0]))[0];
  if (busiest && busiest[1].length >= 3) {
    insights.push({
      id: `cadence-${busiest[0]}`,
      kind: "cadence",
      title: `${busiest[0]} 时有 ${busiest[1].length} 条消息`,
      detail: "按导出时间归到同一小时。",
      evidence: evidence(busiest[1].slice(0, 3)),
    });
  }
  for (const message of chat.messages.filter((item) => item.text.includes("？") || item.text.includes("?")).slice(0, 8)) {
    insights.push({ id: `question-${message.id}`, kind: "question", title: "有待确认的提问", detail: "原文里有提问，没有代为回复。", evidence: evidence([message]) });
  }
  return insights;
}

function evidence(messages) {
  return messages.slice(0, 8).map((message) => ({ messageId: message.id, excerpt: message.text.length <= 120 ? message.text : message.text.slice(0, 120) }));
}

function baseName(name) {
  const value = String(name ?? "");
  const slash = Math.max(value.lastIndexOf("/"), value.lastIndexOf("\\"));
  const base = slash >= 0 ? value.slice(slash + 1) : value;
  if (base === "." || base === ".." || base.length === 0 || base.length > 80 || [...base].some((char) => char.charCodeAt(0) < 32)) return "未命名记录";
  return base;
}

function valid(value, limit) {
  return typeof value === "string" && value.length > 0 && value.length <= limit && ![...value].some((char) => char.charCodeAt(0) < 32);
}
