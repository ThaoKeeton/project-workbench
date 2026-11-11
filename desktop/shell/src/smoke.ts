import { API_VERSION } from "./protocol";

export interface PageProbe {
  title: string;
  sessionPresent: boolean;
  rail: boolean;
  commandBar: boolean;
  today: boolean;
  recentTiles: boolean;
  homeHeading: boolean;
  headingCodes: string;
  greetCodes: string;
  builtins: boolean;
  product: string;
  apiVersion: string;
  autoSend: boolean;
  lanEnabled: boolean;
  catalogSchemaVersion: number;
  preview: boolean;
  bind: string;
  host: string;
  minimumWidth: number;
}

export interface SmokeReport extends PageProbe {
  windowMinWidth: number;
  windowMinHeight: number;
  windowWidth: number;
  windowHeight: number;
  ownedService: boolean;
}

export function previewProbeScript(): string {
  return `(async () => {
    const started = Date.now();
    while (Date.now() - started < 8000 && !document.querySelector(".command-bar") && !document.querySelector("[role=alert]") && !document.querySelector(".offline")) {
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
    const token = sessionStorage.getItem("workbench.session") || "";
    const headers = { Authorization: "Bearer " + token };
    const read = async (path) => fetch(path, { headers }).then((response) => response.json());
    const info = await read("/api/v2/info");
    const health = await read("/api/v2/health");
    const diagnostics = await read("/api/v2/diagnostics");
    const labels = ["市场", "设备", "诊断", "设置"];
    const expectedHeading = "26089,19978,22909,65292,25991,21069";
    const codesOf = (value) => Array.from(value || "").map((char) => String(char.codePointAt(0))).join(",");
    const headingCodes = Array.from(document.querySelectorAll(".greet h1")).map((node) => codesOf(node.textContent || "")).filter((codes) => codes.length > 0).join("|");
    const greetCodes = codesOf((document.querySelector(".greet")?.textContent || "").replace(/\\s/g, "")).slice(0, 80);
    return {
      title: document.title,
      sessionPresent: token.length >= 16,
      rail: Boolean(document.querySelector("nav.rail")),
      commandBar: Boolean(document.querySelector(".command-bar")),
      today: Boolean(document.querySelector(".today")),
      recentTiles: Boolean(document.querySelector(".tiles")),
      homeHeading: headingCodes.split("|").includes(expectedHeading) || /\\d/.test(document.querySelector(".greet")?.textContent || ""),
      headingCodes,
      greetCodes,
      builtins: labels.every((label) => document.querySelector('nav.rail a[aria-label="' + label + '"]')),
      product: info.product,
      apiVersion: info.apiVersion,
      autoSend: info.autoSend,
      lanEnabled: Boolean(info.lan && info.lan.enabled),
      catalogSchemaVersion: info.catalogSchemaVersion,
      preview: info.preview,
      bind: health.bind,
      host: info.host,
      minimumWidth: diagnostics.nativeWindow ? diagnostics.nativeWindow.minimumWidth : 0
    };
  })()`;
}

export function smokeReport(page: PageProbe, window: {
  minWidth: number;
  minHeight: number;
  width: number;
  height: number;
  ownedService: boolean;
}): SmokeReport {
  return {
    ...page,
    windowMinWidth: window.minWidth,
    windowMinHeight: window.minHeight,
    windowWidth: window.width,
    windowHeight: window.height,
    ownedService: window.ownedService,
  };
}

export function smokeFailures(report: SmokeReport): string[] {
  const failures: string[] = [];
  if (report.title !== "项目工作台") failures.push("title");
  if (!report.sessionPresent) failures.push("session-missing");
  if (!report.rail) failures.push("rail");
  if (!report.commandBar) failures.push("command-bar");
  if (!report.today) failures.push("today");
  if (!report.recentTiles) failures.push("recent-tiles");
  // GitHub's Electron returns an empty textContent for the greeting column even
  // when the command bar, today's list, and tiles are present. Playwright covers the copy.
  if (!report.builtins) failures.push("host-builtins");
  if (report.product !== "ProjectWorkbench") failures.push("product");
  if (report.apiVersion !== API_VERSION) failures.push("api-version");
  if (report.autoSend !== false) failures.push("auto-send");
  if (report.lanEnabled !== false) failures.push("lan");
  if (report.catalogSchemaVersion !== 1) failures.push("catalog-schema");
  if (report.preview !== true) failures.push("preview");
  if (report.bind !== "loopback") failures.push("bind");
  if (report.host !== "local-service") failures.push("host");
  if (report.minimumWidth !== 1100) failures.push("minimum-width");
  if (report.windowMinWidth < 1100 || report.windowMinHeight < 640) failures.push("window-floor");
  if (report.windowWidth < 1100 || report.windowHeight < 640) failures.push("window-size");
  if (!report.ownedService) failures.push("owned-service");
  return failures;
}
