import { app, BrowserWindow, ipcMain, nativeTheme, session } from "electron";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import {
  WINDOW_DEFAULT_HEIGHT,
  WINDOW_DEFAULT_WIDTH,
  WINDOW_MIN_HEIGHT,
  WINDOW_MIN_WIDTH,
  findPackagedService,
  findRepoRoot,
  navigationAllowed,
  pageBackground,
  parseShellFlags,
  publicErrorCode,
  redactDiagnostic,
  resolveDotnet,
  resolveLocalAppData,
  resolveUiDist,
  resolveUserRoot,
} from "./protocol";
import { previewProbeScript, smokeFailures, smokeReport, type PageProbe } from "./smoke";
import { attachOrStart, type RunningService } from "./supervisor";

const flags = (() => {
  try {
    return parseShellFlags(process.argv.slice(1));
  } catch (error) {
    console.error(publicErrorCode(error));
    process.exit(1);
  }
})();
const userRoot = (() => {
  try {
    const localAppData = resolveLocalAppData(process.env, process.platform, os.homedir());
    if (!localAppData) throw new Error("local-app-data-missing");
    const root = resolveUserRoot(flags.userRoot, localAppData, process.platform);
    fs.mkdirSync(path.join(root, "electron"), { recursive: true });
    return root;
  } catch (error) {
    console.error(publicErrorCode(error));
    process.exit(1);
  }
})();

app.setName("workbench-v2-preview");
app.setPath("userData", path.join(userRoot, "electron"));
if (flags.smoke || process.env.WORKBENCH_ELECTRON_DISABLE_GPU === "1") {
  app.disableHardwareAcceleration();
  app.commandLine.appendSwitch("disable-gpu");
}

let running: RunningService | undefined;
let stopping = false;

async function shutdown(code: number): Promise<void> {
  if (stopping) return;
  stopping = true;
  try {
    await running?.stop();
  } catch {
    code = code === 0 ? 1 : code;
  }
  app.exit(code);
}

function reportFailure(error: unknown): void {
  const code = publicErrorCode(error);
  console.error(code);
  const diagnostic = error instanceof Error ? (error as Error & { diagnostic?: string }).diagnostic : undefined;
  if (diagnostic && diagnostic.trim().length > 0) console.error(diagnostic);
}

app.on("window-all-closed", () => {
  void shutdown(0);
});
process.on("SIGINT", () => {
  void shutdown(0);
});
process.on("SIGTERM", () => {
  void shutdown(0);
});
process.on("SIGHUP", () => {
  void shutdown(0);
});

app.whenReady().then(async () => {
  try {
    const packagedService = findPackagedService([path.dirname(process.execPath), __dirname], process.platform);
    const repoRoot = packagedService ? path.dirname(packagedService) : findRepoRoot(__dirname);
    const uiDist = resolveUiDist(flags.uiDist ?? (packagedService ? path.join(repoRoot, "ui") : undefined), repoRoot);
    running = await attachOrStart({
      repoRoot,
      projectPath: packagedService ?? (flags.serviceProject ? path.resolve(flags.serviceProject) : path.join(repoRoot, "src", "Workbench.Service", "Workbench.Service.csproj")),
      userRoot,
      v1Plugins: flags.v1Plugins ? path.resolve(flags.v1Plugins) : undefined,
      v2Plugins: flags.v2Plugins ? path.resolve(flags.v2Plugins) : undefined,
      uiDist,
      attach: flags.attach,
      dotnetPath: packagedService ?? resolveDotnet(process.env, process.platform),
      serviceExecutable: packagedService ?? undefined,
    });
    const window = openWindow(running);
    await withTimeout(window.loadURL(running.baseUrl), 30_000, "page-load-timeout");
    if (!flags.smoke) return;
    const page = await withTimeout(window.webContents.executeJavaScript(previewProbeScript()) as Promise<PageProbe>, 20_000, "probe-timeout");
    const [minWidth, minHeight] = window.getMinimumSize();
    const [width, height] = window.getSize();
    const report = smokeReport(page, {
      minWidth,
      minHeight,
      width,
      height,
      ownedService: running.owned,
    });
    const failures = smokeFailures(report);
    if (flags.smokeReport) {
      fs.mkdirSync(path.dirname(path.resolve(flags.smokeReport)), { recursive: true });
      fs.writeFileSync(path.resolve(flags.smokeReport), `${JSON.stringify({ ok: failures.length === 0, failures, report }, null, 2)}\n`, { mode: 0o600 });
    }
    if (failures.length > 0) console.error(failures.join(","));
    await shutdown(failures.length === 0 ? 0 : 2);
  } catch (error) {
    reportFailure(error);
    await shutdown(1);
  }
}).catch((error: unknown) => {
  reportFailure(error);
  void shutdown(1);
});

function openWindow(service: RunningService): BrowserWindow {
  const origin = new URL(service.baseUrl).origin;
  let window: BrowserWindow | undefined;
  ipcMain.on("workbench:session-token", (event) => {
    event.returnValue = window !== undefined && event.sender === window.webContents ? service.token : "";
  });
  session.defaultSession.setPermissionRequestHandler((_contents, _permission, callback) => {
    callback(false);
  });
  window = new BrowserWindow({
    width: WINDOW_DEFAULT_WIDTH,
    height: WINDOW_DEFAULT_HEIGHT,
    minWidth: WINDOW_MIN_WIDTH,
    minHeight: WINDOW_MIN_HEIGHT,
    title: "项目工作台",
    backgroundColor: pageBackground(nativeTheme.shouldUseDarkColors),
    autoHideMenuBar: true,
    show: true,
    webPreferences: {
      preload: path.join(__dirname, "..", "preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
      allowRunningInsecureContent: false,
    },
  });
  window.setMinimumSize(WINDOW_MIN_WIDTH, WINDOW_MIN_HEIGHT);
  window.setMenuBarVisibility(false);
  window.webContents.setWindowOpenHandler(() => ({ action: "deny" }));
  window.webContents.on("will-navigate", (event, url) => {
    if (!navigationAllowed(url, origin)) event.preventDefault();
  });
  window.webContents.on("will-redirect", (event, url) => {
    if (!navigationAllowed(url, origin)) event.preventDefault();
  });
  window.webContents.on("did-fail-load", (_event, errorCode, _description, _url, isMainFrame) => {
    if (isMainFrame) console.error(`page-load-failed:${errorCode}`);
  });
  window.webContents.on("console-message", (...args: unknown[]) => {
    const message = args.map((part) => String(part)).join(" ");
    if (!/error|uncaught|failed/i.test(message)) return;
    console.error(redactDiagnostic(message, [service.token]));
  });
  return window;
}

function withTimeout<T>(work: Promise<T>, ms: number, code: string): Promise<T> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(code)), ms);
    work.then(
      (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      (error: unknown) => {
        clearTimeout(timer);
        reject(error instanceof Error ? error : new Error(code));
      },
    );
  });
}
