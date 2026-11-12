import fs from "node:fs";
import path from "node:path";

export const WINDOW_DEFAULT_WIDTH = 1180;
export const WINDOW_DEFAULT_HEIGHT = 800;
export const WINDOW_MIN_WIDTH = 1100;
export const WINDOW_MIN_HEIGHT = 640;
export const PREVIEW_ROOT_NAME = "ProjectWorkbench-v2-preview";
export const OFFICIAL_ROOT_NAME = "ProjectWorkbench";
export const SESSION_SCHEMA = "workbench.v2-session.preview";
export const SESSION_FILE = "v2-session.json";
export const SERVICE_PID_SCHEMA = "workbench.v2-service-pid.preview";
export const SERVICE_PID_FILE = "v2-service.pid.json";
export const API_VERSION = "2.0.0-preview.1";

const VALUE_FLAGS = new Set([
  "--user-root",
  "--v1-plugins",
  "--v2-plugins",
  "--ui-dist",
  "--smoke-report",
  "--service-project",
]);
const BOOL_FLAGS = new Set(["--attach", "--smoke"]);
const IGNORED_FLAGS = new Set([
  "--no-sandbox",
  "--disable-gpu",
  "--disable-dev-shm-usage",
  "--enable-logging",
  "--disable-software-rasterizer",
  "--force-color-profile",
]);

export type ShellCode =
  | "allow-lan-rejected"
  | "official-user-root-rejected"
  | "local-app-data-missing"
  | "invalid-arguments"
  | "invalid-session";

export class ShellRejected extends Error {
  readonly code: ShellCode;

  constructor(code: ShellCode) {
    super(code);
    this.name = "ShellRejected";
    this.code = code;
  }
}

export interface RawShellFlags {
  userRoot?: string;
  v1Plugins?: string;
  v2Plugins?: string;
  uiDist?: string;
  smokeReport?: string;
  serviceProject?: string;
  attach: boolean;
  smoke: boolean;
}

export function parseShellFlags(argv: readonly string[]): RawShellFlags {
  const flags: RawShellFlags = { attach: false, smoke: false };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index] ?? "";
    if (arg === "--") continue;
    if (arg === "--allow-lan" || arg.startsWith("--allow-lan=")) throw new ShellRejected("allow-lan-rejected");
    if (!arg.startsWith("--")) continue;
    const equals = arg.indexOf("=");
    const name = equals === -1 ? arg : arg.slice(0, equals);
    if (IGNORED_FLAGS.has(name) || name.startsWith("--inspect")) continue;
    if (BOOL_FLAGS.has(name)) {
      if (equals !== -1) throw new ShellRejected("invalid-arguments");
      if (name === "--attach") flags.attach = true;
      if (name === "--smoke") flags.smoke = true;
      continue;
    }
    if (!VALUE_FLAGS.has(name)) throw new ShellRejected("invalid-arguments");
    const value = equals === -1 ? argv[index + 1] : arg.slice(equals + 1);
    if (equals === -1) index += 1;
    if (!value || value.startsWith("--")) throw new ShellRejected("invalid-arguments");
    assignFlag(flags, name, value);
  }
  return flags;
}

function assignFlag(flags: RawShellFlags, name: string, value: string): void {
  if (name === "--user-root") flags.userRoot = value;
  if (name === "--v1-plugins") flags.v1Plugins = value;
  if (name === "--v2-plugins") flags.v2Plugins = value;
  if (name === "--ui-dist") flags.uiDist = value;
  if (name === "--smoke-report") flags.smokeReport = value;
  if (name === "--service-project") flags.serviceProject = value;
}

export function resolveLocalAppData(env: NodeJS.ProcessEnv, platform: NodeJS.Platform, home: string): string | undefined {
  if (platform === "win32") {
    const value = env.LOCALAPPDATA?.trim();
    return value ? value : undefined;
  }
  const xdg = env.XDG_DATA_HOME?.trim();
  if (xdg) return xdg;
  if (!home) return undefined;
  return path.join(home, ".local", "share");
}

export function existingRealpath(
  candidate: string,
  realpath: (target: string) => string,
  exists: (target: string) => boolean,
): string {
  const resolved = path.resolve(candidate);
  const missing: string[] = [];
  let current = resolved;
  while (!exists(current)) {
    const parent = path.dirname(current);
    if (parent === current) return resolved;
    missing.push(path.basename(current));
    current = parent;
  }
  let real = realpath(current);
  for (const part of missing.reverse()) real = path.join(real, part);
  return real;
}

export function isInsidePath(child: string, parent: string, platform: NodeJS.Platform): boolean {
  const api = platform === "win32" ? path.win32 : path.posix;
  const left = platform === "win32" ? api.resolve(child).toLowerCase() : api.resolve(child);
  const right = platform === "win32" ? api.resolve(parent).toLowerCase() : api.resolve(parent);
  const parentPrefix = right.endsWith(api.sep) ? right : right + api.sep;
  return left === right || left.startsWith(parentPrefix);
}

export function officialUserRoot(localAppData: string): string {
  return path.resolve(localAppData, OFFICIAL_ROOT_NAME);
}

export function resolveUserRoot(
  requested: string | undefined,
  localAppData: string,
  platform: NodeJS.Platform,
  realpath: (target: string) => string = fs.realpathSync,
  exists: (target: string) => boolean = fs.existsSync,
): string {
  if (!localAppData.trim()) throw new ShellRejected("local-app-data-missing");
  const candidate = path.resolve(requested ?? path.join(localAppData, PREVIEW_ROOT_NAME));
  const resolved = existingRealpath(candidate, realpath, exists);
  const official = existingRealpath(officialUserRoot(localAppData), realpath, exists);
  if (isInsidePath(resolved, official, platform)) throw new ShellRejected("official-user-root-rejected");
  return resolved;
}

export function isAcceptableToken(token: string): boolean {
  return token.length >= 16 && token.length <= 128 && /^[0-9A-Za-z-]+$/.test(token);
}

export function isLoopbackBaseUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === "http:"
      && url.hostname === "127.0.0.1"
      && url.port.length > 0
      && (url.pathname === "/" || url.pathname === "")
      && url.username.length === 0
      && url.password.length === 0
      && url.search.length === 0
      && url.hash.length === 0;
  } catch {
    return false;
  }
}

export function sessionDocument(baseUrl: string, token: string): string {
  if (!isLoopbackBaseUrl(baseUrl) || !isAcceptableToken(token)) throw new ShellRejected("invalid-session");
  return JSON.stringify({ schema: SESSION_SCHEMA, schemaVersion: 2, baseUrl, token });
}

export function readSessionDocument(raw: string): { baseUrl: string; token: string } | null {
  if (raw.length === 0 || raw.length > 4096) return null;
  try {
    const parsed = JSON.parse(raw) as { schema?: unknown; schemaVersion?: unknown; baseUrl?: unknown; token?: unknown };
    if (parsed.schema !== SESSION_SCHEMA || parsed.schemaVersion !== 2) return null;
    if (typeof parsed.baseUrl !== "string" || typeof parsed.token !== "string") return null;
    if (!isLoopbackBaseUrl(parsed.baseUrl) || !isAcceptableToken(parsed.token)) return null;
    return { baseUrl: parsed.baseUrl, token: parsed.token };
  } catch {
    return null;
  }
}

export function servicePidDocument(pid: number): string {
  if (!Number.isInteger(pid) || pid <= 1) throw new ShellRejected("invalid-arguments");
  return JSON.stringify({ schema: SERVICE_PID_SCHEMA, schemaVersion: 2, pid });
}

export function readServicePid(raw: string): number | null {
  if (raw.length === 0 || raw.length > 512) return null;
  try {
    const parsed = JSON.parse(raw) as { schema?: unknown; schemaVersion?: unknown; pid?: unknown };
    if (parsed.schema !== SERVICE_PID_SCHEMA || parsed.schemaVersion !== 2) return null;
    if (typeof parsed.pid !== "number" || !Number.isInteger(parsed.pid) || parsed.pid <= 1) return null;
    return parsed.pid;
  } catch {
    return null;
  }
}

export class ServiceOutputParser {
  private buffer = "";
  listening?: string;
  token?: string;

  push(chunk: string): void {
    this.buffer += chunk;
    if (this.buffer.length > 65536) this.buffer = this.buffer.slice(-4096);
    let newline = this.buffer.indexOf("\n");
    while (newline >= 0) {
      const line = this.buffer.slice(0, newline).replace(/\r$/, "");
      this.buffer = this.buffer.slice(newline + 1);
      this.consume(line);
      newline = this.buffer.indexOf("\n");
    }
  }

  private consume(line: string): void {
    if (line.startsWith("listening: ")) {
      const url = line.slice("listening: ".length).trim();
      if (isLoopbackBaseUrl(url)) this.listening = url;
      return;
    }
    if (line.startsWith("session-token: ")) {
      const token = line.slice("session-token: ".length).trim();
      if (isAcceptableToken(token)) this.token = token;
    }
  }
}

export function directServiceArgs(options: {
  userRoot: string;
  v1Plugins?: string;
  v2Plugins?: string;
  uiDist?: string;
}): string[] {
  const serviceArgs = ["--user-root", options.userRoot, "--print-token"];
  if (options.v1Plugins) serviceArgs.push("--v1-plugins", options.v1Plugins);
  if (options.v2Plugins) serviceArgs.push("--v2-plugins", options.v2Plugins);
  if (options.uiDist) serviceArgs.push("--ui-dist", options.uiDist);
  if (serviceArgs.includes("--allow-lan") || serviceArgs.some((arg) => arg.startsWith("--allow-lan="))) {
    throw new ShellRejected("allow-lan-rejected");
  }
  return serviceArgs;
}

export function serviceLaunchArgs(options: {
  projectPath: string;
  userRoot: string;
  v1Plugins?: string;
  v2Plugins?: string;
  uiDist?: string;
}): string[] {
  return ["run", "--project", options.projectPath, "-c", "Release", "--no-launch-profile", "--", ...directServiceArgs(options)];
}

export function findPackagedService(
  starts: readonly string[],
  platform: NodeJS.Platform,
  exists: (target: string) => boolean = fs.existsSync,
): string | null {
  const fileName = platform === "win32" ? "Workbench.Service.exe" : "Workbench.Service";
  const seen = new Set<string>();
  for (const start of starts) {
    if (!start) continue;
    let current = path.resolve(start);
    for (let depth = 0; depth < 6; depth += 1) {
      if (seen.has(current)) break;
      seen.add(current);
      const candidate = path.join(current, "service", fileName);
      if (exists(candidate)) return candidate;
      const parent = path.dirname(current);
      if (parent === current) break;
      current = parent;
    }
  }
  return null;
}

export function resolveDotnet(env: NodeJS.ProcessEnv, platform: NodeJS.Platform): string {
  const api = platform === "win32" ? path.win32 : path.posix;
  const name = platform === "win32" ? "dotnet.exe" : "dotnet";
  if (env.DOTNET_ROOT && env.DOTNET_ROOT.trim().length > 0) return api.join(env.DOTNET_ROOT, name);
  const candidates: string[] = [];
  const home = platform === "win32" ? env.USERPROFILE : env.HOME;
  if (home && home.trim().length > 0) candidates.push(api.join(home, ".dotnet", name));
  if (platform === "win32") {
    const programFiles = env.ProgramFiles && env.ProgramFiles.trim().length > 0 ? env.ProgramFiles : "C:\\Program Files";
    candidates.push(api.join(programFiles, "dotnet", name));
  } else {
    candidates.push(`/usr/share/dotnet/${name}`, `/usr/bin/${name}`);
  }
  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) return candidate;
  }
  return candidates[0] ?? name;
}

export function windowsExecutable(file: string, systemRoot = "C:\\Windows"): string {
  return path.win32.normalize(path.win32.join(systemRoot, "System32", file));
}

export function findRepoRoot(start: string, exists: (target: string) => boolean = fs.existsSync): string {
  let current = path.resolve(start);
  for (let depth = 0; depth < 8; depth += 1) {
    if (exists(path.join(current, "src", "Workbench.Service", "Workbench.Service.csproj"))) return current;
    const parent = path.dirname(current);
    if (parent === current) break;
    current = parent;
  }
  throw new Error("repo-root-missing");
}

export function resolveUiDist(requested: string | undefined, repoRoot: string, exists: (target: string) => boolean = fs.existsSync): string {
  const candidate = path.resolve(requested ?? path.join(repoRoot, "web", "host", "dist"));
  if (!exists(path.join(candidate, "index.html"))) throw new Error("ui-dist-missing");
  return candidate;
}

export function pageBackground(dark: boolean): string {
  return dark ? "#0B0C0E" : "#F3F5F7";
}

export function navigationAllowed(target: string, origin: string): boolean {
  try {
    return new URL(target).origin === origin;
  } catch {
    return false;
  }
}

export function sessionInstallSource(token: string): string {
  if (!isAcceptableToken(token)) throw new ShellRejected("invalid-session");
  return `if (location.protocol === "http:" && location.hostname === "127.0.0.1") { sessionStorage.setItem("workbench.session", ${JSON.stringify(token)}); }`;
}

export function shouldRetireServiceProcess(commandLine: string): boolean {
  if (!commandLine.includes("Workbench.Service")) return false;
  if (commandLine.includes("Workbench.App")) return false;
  if (commandLine.includes("--allow-lan")) return false;
  return true;
}

export function redactDiagnostic(text: string, secrets: readonly string[]): string {
  let redacted = text;
  for (const secret of secrets) {
    if (secret.length >= 8) redacted = redacted.split(secret).join("[redacted]");
  }
  return redacted
    .split(/\r?\n/)
    .filter((line) => !line.includes("session-token"))
    .slice(-30)
    .join("\n")
    .slice(-4000);
}

export function publicErrorCode(error: unknown): string {
  if (error instanceof ShellRejected) return error.code;
  if (error instanceof Error && /^[a-z0-9-]{3,64}$/.test(error.message)) return error.message;
  return "shell-failed";
}
