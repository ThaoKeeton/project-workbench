import { spawn, spawnSync, type ChildProcess } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import {
  SERVICE_PID_FILE,
  SESSION_FILE,
  ServiceOutputParser,
  ShellRejected,
  readServicePid,
  readSessionDocument,
  redactDiagnostic,
  directServiceArgs,
  serviceLaunchArgs,
  servicePidDocument,
  sessionDocument,
  shouldRetireServiceProcess,
  windowsExecutable,
} from "./protocol";

export interface ProcessControl {
  signal(pid: number, signal: NodeJS.Signals, group: boolean): void;
  commandLine(pid: number): string;
}

export interface ServiceChild {
  pid?: number;
  stdout: NodeJS.ReadableStream | null;
  stderr: NodeJS.ReadableStream | null;
  once(event: "exit", listener: (code: number | null) => void): void;
}

export type SpawnService = (
  command: string,
  args: readonly string[],
  cwd: string,
) => ServiceChild;

export interface RunningService {
  baseUrl: string;
  token: string;
  owned: boolean;
  stop: () => Promise<void>;
}

export interface ServiceStartOptions {
  repoRoot: string;
  projectPath: string;
  userRoot: string;
  v1Plugins?: string;
  v2Plugins?: string;
  uiDist?: string;
  attach: boolean;
  dotnetPath: string;
  serviceExecutable?: string;
  fetchImpl?: typeof fetch;
  spawnImpl?: SpawnService;
  control?: ProcessControl;
  selfPid?: number;
}

export function hostProcessControl(platform: NodeJS.Platform): ProcessControl {
  return {
    signal(pid: number, signal: NodeJS.Signals, group: boolean): void {
      if (!Number.isInteger(pid) || pid <= 1) return;
      if (platform === "win32") {
        const taskkill = windowsExecutable("taskkill.exe", process.env.SystemRoot || "C:\\Windows");
        spawnSync(taskkill, ["/pid", String(pid), "/t", "/f"], { windowsHide: true, stdio: "ignore" });
        return;
      }
      try {
        process.kill(group ? -pid : pid, signal);
      } catch {
        try {
          process.kill(pid, signal);
        } catch {
          // The process already exited.
        }
      }
    },
    commandLine(pid: number): string {
      if (!Number.isInteger(pid) || pid <= 1) return "";
      if (platform === "linux") {
        try {
          return fs.readFileSync(`/proc/${pid}/cmdline`).toString("utf8").replaceAll("\0", " ");
        } catch {
          return "";
        }
      }
      if (platform === "win32") {
        const powershell = windowsExecutable("WindowsPowerShell/v1.0/powershell.exe", process.env.SystemRoot || "C:\\Windows");
        const result = spawnSync(
          powershell,
          ["-NoProfile", "-Command", `(Get-CimInstance Win32_Process -Filter "ProcessId=${pid}").CommandLine`],
          { encoding: "utf8", windowsHide: true, timeout: 5000 },
        );
        return result.stdout ?? "";
      }
      return "";
    },
  };
}

export function defaultSpawn(command: string, args: readonly string[], cwd: string): ServiceChild {
  const child: ChildProcess = spawn(command, args, {
    cwd,
    detached: process.platform !== "win32",
    windowsHide: true,
    stdio: ["ignore", "pipe", "pipe"],
    env: {
      ...process.env,
      DOTNET_CLI_TELEMETRY_OPTOUT: "1",
      DOTNET_NOLOGO: "1",
      DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1",
    },
  });
  return child as ServiceChild;
}

export async function attachOrStart(options: ServiceStartOptions): Promise<RunningService> {
  if (options.attach) {
    const attached = await tryAttach(options.userRoot, options.fetchImpl ?? fetch);
    if (attached) return attached;
  }
  await retireStaleService(options);
  return startOwnedService(options);
}

export async function tryAttach(userRoot: string, fetchImpl: typeof fetch): Promise<RunningService | null> {
  let raw = "";
  try {
    raw = await fs.promises.readFile(path.join(userRoot, SESSION_FILE), "utf8");
  } catch {
    return null;
  }
  const session = readSessionDocument(raw);
  if (!session) return null;
  try {
    const response = await fetchImpl(new URL("/api/v2/health", session.baseUrl), {
      headers: { Authorization: `Bearer ${session.token}` },
      redirect: "manual",
      signal: AbortSignal.timeout(2000),
    });
    if (response.status !== 200) return null;
    const body = await response.json() as { status?: unknown; bind?: unknown; apiVersion?: unknown };
    if (body.status !== "ok" || body.bind !== "loopback" || body.apiVersion !== "2.0.0-preview.1") return null;
    return { baseUrl: session.baseUrl, token: session.token, owned: false, stop: async () => undefined };
  } catch {
    return null;
  }
}

export async function retireStaleService(options: Pick<ServiceStartOptions, "userRoot" | "control" | "selfPid">): Promise<void> {
  const control = options.control ?? hostProcessControl(process.platform);
  const selfPid = options.selfPid ?? process.pid;
  const pidPath = path.join(options.userRoot, SERVICE_PID_FILE);
  let raw = "";
  try {
    raw = await fs.promises.readFile(pidPath, "utf8");
  } catch {
    return;
  }
  const pid = readServicePid(raw);
  if (pid === null || pid === selfPid) return;
  const commandLine = control.commandLine(pid);
  if (!commandLine) {
    await fs.promises.rm(pidPath, { force: true });
    return;
  }
  if (!shouldRetireServiceProcess(commandLine)) return;
  control.signal(pid, "SIGTERM", true);
  await fs.promises.rm(pidPath, { force: true });
}

export async function startOwnedService(options: ServiceStartOptions): Promise<RunningService> {
  const shared = {
    userRoot: options.userRoot,
    v1Plugins: options.v1Plugins,
    v2Plugins: options.v2Plugins,
    uiDist: options.uiDist,
  };
  const command = options.serviceExecutable ?? options.dotnetPath;
  const args = options.serviceExecutable
    ? directServiceArgs(shared)
    : serviceLaunchArgs({ projectPath: options.projectPath, ...shared });
  const spawnImpl = options.spawnImpl ?? defaultSpawn;
  const control = options.control ?? hostProcessControl(process.platform);
  const child = spawnImpl(command, args, options.repoRoot);
  const parser = new ServiceOutputParser();
  let stderr = "";
  let exited: number | null = null;
  child.stdout?.on("data", (chunk: Buffer | string) => parser.push(chunk.toString()));
  child.stderr?.on("data", (chunk: Buffer | string) => {
    stderr += chunk.toString();
    if (stderr.length > 16000) stderr = stderr.slice(-8000);
  });
  child.once("exit", (code) => {
    exited = code ?? 1;
  });

  const ready = await waitForReady(parser, () => exited);
  if (!ready) {
    control.signal(child.pid ?? -1, "SIGTERM", process.platform !== "win32");
    const code = exited === null ? "service-start-timeout" : "service-start-failed";
    const error = new Error(code);
    (error as Error & { diagnostic?: string }).diagnostic = redactDiagnostic(stderr, [parser.token ?? ""]);
    throw error;
  }

  try {
    await writePrivateFile(path.join(options.userRoot, SESSION_FILE), sessionDocument(ready.listening, ready.token));
    if (child.pid !== undefined) {
      await writePrivateFile(path.join(options.userRoot, SERVICE_PID_FILE), servicePidDocument(child.pid));
    }
  } catch (error) {
    if (child.pid !== undefined) control.signal(child.pid, "SIGTERM", process.platform !== "win32");
    if (error instanceof ShellRejected) throw error;
    throw new Error("session-file-failed");
  }
  let stopped = false;
  return {
    baseUrl: ready.listening,
    token: ready.token,
    owned: true,
    stop: async () => {
      if (stopped) return;
      stopped = true;
      if (exited === null && child.pid !== undefined) control.signal(child.pid, "SIGTERM", process.platform !== "win32");
      await waitForExit(() => exited);
      await fs.promises.rm(path.join(options.userRoot, SERVICE_PID_FILE), { force: true });
    },
  };
}

async function waitForReady(
  parser: ServiceOutputParser,
  exited: () => number | null,
): Promise<{ listening: string; token: string } | null> {
  const configured = Number(process.env.WORKBENCH_SERVICE_START_TIMEOUT_MS);
  const deadline = Date.now() + (Number.isFinite(configured) && configured >= 1_000 ? configured : 240_000);
  while (Date.now() < deadline) {
    if (parser.listening && parser.token) return { listening: parser.listening, token: parser.token };
    if (exited() !== null) return null;
    await delay(25);
  }
  return parser.listening && parser.token ? { listening: parser.listening, token: parser.token } : null;
}

async function waitForExit(exited: () => number | null): Promise<void> {
  const deadline = Date.now() + 5_000;
  while (exited() === null && Date.now() < deadline) await delay(25);
}

async function writePrivateFile(file: string, contents: string): Promise<void> {
  await fs.promises.mkdir(path.dirname(file), { recursive: true, mode: 0o700 });
  const temporary = `${file}.${process.pid}.tmp`;
  await fs.promises.writeFile(temporary, contents, { mode: 0o600, flag: "w" });
  await fs.promises.chmod(temporary, 0o600).catch(() => undefined);
  await fs.promises.rename(temporary, file);
  await fs.promises.chmod(file, 0o600).catch(() => undefined);
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export function assertNoLan(args: readonly string[]): void {
  if (args.includes("--allow-lan") || args.some((arg) => arg.startsWith("--allow-lan="))) {
    throw new ShellRejected("allow-lan-rejected");
  }
}
