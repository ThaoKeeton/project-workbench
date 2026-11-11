import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import {
  API_VERSION,
  PREVIEW_ROOT_NAME,
  SESSION_SCHEMA,
  ServiceOutputParser,
  ShellRejected,
  WINDOW_MIN_HEIGHT,
  WINDOW_MIN_WIDTH,
  findRepoRoot,
  isAcceptableToken,
  isInsidePath,
  isLoopbackBaseUrl,
  navigationAllowed,
  pageBackground,
  parseShellFlags,
  publicErrorCode,
  readServicePid,
  readSessionDocument,
  redactDiagnostic,
  resolveDotnet,
  resolveLocalAppData,
  resolveUiDist,
  resolveUserRoot,
  serviceLaunchArgs,
  servicePidDocument,
  sessionDocument,
  sessionInstallSource,
  shouldRetireServiceProcess,
} from "./protocol";

const token = "a".repeat(32);
const tempDirs: string[] = [];

afterEach(() => {
  for (const dir of tempDirs.splice(0)) fs.rmSync(dir, { recursive: true, force: true });
});

function tempDir(): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "wb-shell-"));
  tempDirs.push(dir);
  return dir;
}

describe("shell flags", () => {
  it("rejects LAN and unknown flags before any service argument is built", () => {
    expect(() => parseShellFlags(["--allow-lan"])).toThrow(ShellRejected);
    expect(() => parseShellFlags(["--allow-lan=true"])).toThrowError("allow-lan-rejected");
    expect(() => parseShellFlags(["--user-root"])).toThrowError("invalid-arguments");
    expect(() => parseShellFlags(["--open-webhook"])).toThrowError("invalid-arguments");
    const flags = parseShellFlags([".", "--no-sandbox", "--ui-dist=/tmp/ui", "--attach", "--smoke"]);
    expect(flags).toMatchObject({ uiDist: "/tmp/ui", attach: true, smoke: true });
  });

  it("keeps the preview root beside the official root", () => {
    const local = tempDir();
    const official = path.join(local, "ProjectWorkbench");
    fs.mkdirSync(official, { recursive: true });
    fs.symlinkSync(official, path.join(local, "alias"), "dir");
    expect(() => resolveUserRoot(official, local, "linux")).toThrowError("official-user-root-rejected");
    expect(() => resolveUserRoot(path.join(official, "data"), local, "linux")).toThrowError("official-user-root-rejected");
    expect(() => resolveUserRoot(path.join(local, "alias"), local, "linux")).toThrowError("official-user-root-rejected");
    const preview = resolveUserRoot(undefined, local, "linux");
    expect(path.basename(preview)).toBe(PREVIEW_ROOT_NAME);
    expect(isInsidePath(preview, official, "linux")).toBe(false);
  });

  it("treats Windows official roots case-insensitively and leaves the preview sibling alone", () => {
    const official = "C:\\Users\\A\\AppData\\Local\\ProjectWorkbench";
    expect(isInsidePath("C:\\Users\\A\\AppData\\Local\\projectworkbench\\data", official, "win32")).toBe(true);
    expect(isInsidePath("C:\\Users\\A\\AppData\\Local\\ProjectWorkbench-v2-preview", official, "win32")).toBe(false);
  });

  it("uses LOCALAPPDATA on Windows and the XDG data home elsewhere", () => {
    expect(resolveLocalAppData({ LOCALAPPDATA: "D:\\AppData\\Local" }, "win32", "C:\\Users\\A")).toBe("D:\\AppData\\Local");
    expect(resolveLocalAppData({}, "win32", "C:\\Users\\A")).toBeUndefined();
    expect(resolveLocalAppData({ XDG_DATA_HOME: "/tmp/share" }, "linux", "/home/a")).toBe("/tmp/share");
    expect(resolveLocalAppData({}, "linux", "/home/a")).toBe(path.join("/home/a", ".local", "share"));
  });
});

describe("loopback session", () => {
  it("accepts only a 127.0.0.1 session document", () => {
    expect(isAcceptableToken(token)).toBe(true);
    expect(isAcceptableToken("short")).toBe(false);
    expect(isAcceptableToken(`${token}!`)).toBe(false);
    expect(isLoopbackBaseUrl("http://127.0.0.1:4317")).toBe(true);
    expect(isLoopbackBaseUrl("http://127.0.0.1:4317/")).toBe(true);
    expect(isLoopbackBaseUrl("http://localhost:4317")).toBe(false);
    expect(isLoopbackBaseUrl("http://127.0.0.1:4317/extra")).toBe(false);
    expect(isLoopbackBaseUrl("http://127.0.0.1.evil.test:4317")).toBe(false);
    expect(isLoopbackBaseUrl("http://user:pass@127.0.0.1:4317")).toBe(false);
    expect(isLoopbackBaseUrl("https://127.0.0.1:4317")).toBe(false);
    const document = sessionDocument("http://127.0.0.1:4317", token);
    expect(document).toBe(`{"schema":"${SESSION_SCHEMA}","schemaVersion":2,"baseUrl":"http://127.0.0.1:4317","token":"${token}"}`);
    expect(readSessionDocument(document)).toEqual({ baseUrl: "http://127.0.0.1:4317", token });
    expect(readSessionDocument(document.replace("4317", "localhost"))).toBeNull();
    expect(readSessionDocument("[]")).toBeNull();
    expect(sessionInstallSource(token)).toContain(JSON.stringify(token));
    expect(sessionInstallSource(token)).not.toContain("localhost");
  });

  it("parses service output without keeping unrelated lines", () => {
    const parser = new ServiceOutputParser();
    parser.push("Build started.\nlisten");
    parser.push(`ing: http://127.0.0.1:5150\nsession-token: ${token}\n`);
    expect(parser.listening).toBe("http://127.0.0.1:5150");
    expect(parser.token).toBe(token);
    const rejected = new ServiceOutputParser();
    rejected.push("listening: http://10.0.0.8:5150\nsession-token: short\n");
    expect(rejected.listening).toBeUndefined();
    expect(rejected.token).toBeUndefined();
  });

  it("builds a loopback-only dotnet run command", () => {
    const args = serviceLaunchArgs({
      projectPath: "/repo/src/Workbench.Service/Workbench.Service.csproj",
      userRoot: "/tmp/preview",
      uiDist: "/repo/web/host/dist",
    });
    expect(args).toEqual([
      "run",
      "--project",
      "/repo/src/Workbench.Service/Workbench.Service.csproj",
      "-c",
      "Release",
      "--no-launch-profile",
      "--",
      "--user-root",
      "/tmp/preview",
      "--print-token",
      "--ui-dist",
      "/repo/web/host/dist",
    ]);
    expect(args).not.toContain("--allow-lan");
    expect(serviceLaunchArgs({
      projectPath: "service.csproj",
      userRoot: "/tmp/preview",
      v1Plugins: "/tmp/v1",
      v2Plugins: "/tmp/v2",
    })).toEqual(expect.arrayContaining(["--v1-plugins", "/tmp/v1", "--v2-plugins", "/tmp/v2"]));
    expect(() => serviceLaunchArgs({ projectPath: "service.csproj", userRoot: "--allow-lan" })).toThrowError("allow-lan-rejected");
    expect(readSessionDocument("{")).toBeNull();
    expect(readSessionDocument("")).toBeNull();
    expect(readServicePid("{")).toBeNull();
    expect(() => servicePidDocument(1)).toThrowError("invalid-arguments");
    expect(resolveDotnet({ DOTNET_ROOT: "/opt/dotnet" }, "linux")).toBe(path.join("/opt/dotnet", "dotnet"));
    expect(resolveDotnet({}, "win32")).toBe("dotnet.exe");
  });
});

describe("shell boundaries", () => {
  it("finds the repo and the built UI without touching the official root", () => {
    const root = tempDir();
    fs.mkdirSync(path.join(root, "src", "Workbench.Service"), { recursive: true });
    fs.writeFileSync(path.join(root, "src", "Workbench.Service", "Workbench.Service.csproj"), "<Project />");
    fs.mkdirSync(path.join(root, "web", "host", "dist"), { recursive: true });
    fs.writeFileSync(path.join(root, "web", "host", "dist", "index.html"), "<div id=\"root\"></div>");
    expect(findRepoRoot(path.join(root, "desktop", "shell", "dist"))).toBe(root);
    expect(resolveUiDist(undefined, root)).toBe(path.join(root, "web", "host", "dist"));
    expect(() => resolveUiDist(undefined, tempDir())).toThrowError("ui-dist-missing");
    expect(() => findRepoRoot(tempDir())).toThrowError("repo-root-missing");
  });

  it("keeps navigation, colors, pid files, and diagnostics inside the preview rules", () => {
    expect(navigationAllowed("http://127.0.0.1:4317/settings", "http://127.0.0.1:4317")).toBe(true);
    expect(navigationAllowed("http://127.0.0.1:4318/", "http://127.0.0.1:4317")).toBe(false);
    expect(navigationAllowed("not a url", "http://127.0.0.1:4317")).toBe(false);
    expect(pageBackground(true)).toBe("#0B0C0E");
    expect(pageBackground(false)).toBe("#F3F5F7");
    expect(WINDOW_MIN_WIDTH).toBe(1100);
    expect(WINDOW_MIN_HEIGHT).toBe(640);
    expect(API_VERSION).toBe("2.0.0-preview.1");
    const pid = servicePidDocument(42);
    expect(readServicePid(pid)).toBe(42);
    expect(readServicePid('{"schema":"other","schemaVersion":2,"pid":42}')).toBeNull();
    expect(shouldRetireServiceProcess("dotnet run --project src/Workbench.Service/Workbench.Service.csproj")).toBe(true);
    expect(shouldRetireServiceProcess("dotnet Workbench.App.dll")).toBe(false);
    expect(shouldRetireServiceProcess("dotnet Workbench.Service.dll --allow-lan")).toBe(false);
    expect(redactDiagnostic(`listening\nsession-token: ${token}\nfailed`, [token])).not.toContain(token);
    expect(redactDiagnostic(`listening\nsession-token: ${token}\nfailed`, [token])).not.toContain("session-token");
    expect(publicErrorCode(new ShellRejected("allow-lan-rejected"))).toBe("allow-lan-rejected");
    expect(publicErrorCode(new Error("C:\\Users\\secret"))).toBe("shell-failed");
  });
});
