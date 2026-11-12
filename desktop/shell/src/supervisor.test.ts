import http from "node:http";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { PassThrough } from "node:stream";
import { afterEach, describe, expect, it } from "vitest";
import { SERVICE_PID_FILE, SESSION_FILE, servicePidDocument, sessionDocument } from "./protocol";
import {
  assertNoLan,
  attachOrStart,
  hostProcessControl,
  retireStaleService,
  tryAttach,
  type ProcessControl,
  type ServiceChild,
} from "./supervisor";
import { ShellRejected } from "./protocol";

const token = "b".repeat(32);
const tempDirs: string[] = [];

afterEach(() => {
  for (const dir of tempDirs.splice(0)) fs.rmSync(dir, { recursive: true, force: true });
});

function tempDir(): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "wb-supervisor-"));
  tempDirs.push(dir);
  return dir;
}

function fakeService(lines: string[]): { child: ServiceChild; control: ProcessControl; signals: number[]; args: string[][]; spawn: (command: string, args: readonly string[], cwd: string) => ServiceChild } {
  const stdout = new PassThrough();
  const stderr = new PassThrough();
  const listeners: Array<(code: number | null) => void> = [];
  const signals: number[] = [];
  const args: string[][] = [];
  const child: ServiceChild = {
    pid: 424242,
    stdout,
    stderr,
    once(event, listener) {
      if (event === "exit") listeners.push(listener);
    },
  };
  const control: ProcessControl = {
    signal(pid) {
      signals.push(pid);
      for (const listener of listeners) listener(0);
    },
    commandLine: () => "",
  };
  return {
    child,
    control,
    signals,
    args,
    spawn(command, spawnArgs) {
      args.push([command, ...spawnArgs]);
      queueMicrotask(() => {
        stdout.write(`${lines.join("\n")}\n`);
      });
      return child;
    },
  };
}

describe("service supervisor", () => {
  it("starts an owned loopback service and removes the pid file on stop", async () => {
    const root = tempDir();
    const fake = fakeService([`listening: http://127.0.0.1:5151`, `session-token: ${token}`]);
    const running = await attachOrStart({
      repoRoot: root,
      projectPath: path.join(root, "Workbench.Service.csproj"),
      userRoot: root,
      uiDist: path.join(root, "dist"),
      attach: false,
      dotnetPath: "dotnet",
      spawnImpl: fake.spawn,
      control: fake.control,
    });
    expect(running.owned).toBe(true);
    expect(running.baseUrl).toBe("http://127.0.0.1:5151");
    expect(fake.args[0]).toContain("--print-token");
    expect(fake.args[0]).not.toContain("--allow-lan");
    const session = fs.readFileSync(path.join(root, SESSION_FILE), "utf8");
    if (!session.includes(token) || session.includes("10.0.0.8")) throw new Error("session-file-mismatch");
    expect((fs.statSync(path.join(root, SESSION_FILE)).mode & 0o777)).toBe(0o600);
    expect(fs.readFileSync(path.join(root, SERVICE_PID_FILE), "utf8")).toContain("424242");
    await running.stop();
    expect(fake.signals).toEqual([424242]);
    expect(fs.existsSync(path.join(root, SERVICE_PID_FILE))).toBe(false);
  });

  it("stops a failed process without writing a session file", async () => {
    const root = tempDir();
    const stdout = new PassThrough();
    const stderr = new PassThrough();
    const listeners: Array<(code: number | null) => void> = [];
    const child: ServiceChild = {
      pid: 100,
      stdout,
      stderr,
      once(event, listener) {
        if (event === "exit") listeners.push(listener);
      },
    };
    const signals: number[] = [];
    await expect(attachOrStart({
      repoRoot: root,
      projectPath: "Workbench.Service.csproj",
      userRoot: root,
      attach: false,
      dotnetPath: "dotnet",
      spawnImpl: () => {
        queueMicrotask(() => {
          stderr.write("service-start-failed\n");
          for (const listener of listeners) listener(1);
        });
        return child;
      },
      control: { signal(pid) { signals.push(pid); }, commandLine: () => "" },
    })).rejects.toThrow("service-start-failed");
    expect(signals).toContain(100);
    expect(fs.existsSync(path.join(root, SESSION_FILE))).toBe(false);
  });

  it("attaches only when the recorded loopback health check matches", async () => {
    const root = tempDir();
    const server = http.createServer((request, response) => {
      const authorized = request.headers.authorization === `Bearer ${token}`;
      response.writeHead(authorized ? 200 : 401, { "content-type": "application/json" });
      response.end(JSON.stringify({ status: "ok", bind: "loopback", apiVersion: "2.0.0-preview.1" }));
    });
    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
    const address = server.address();
    if (!address || typeof address === "string") throw new Error("test-port-missing");
    const baseUrl = `http://127.0.0.1:${address.port}`;
    await fs.promises.writeFile(path.join(root, SESSION_FILE), sessionDocument(baseUrl, token), { mode: 0o600 });
    try {
      const attached = await tryAttach(root, fetch);
      expect(attached?.owned).toBe(false);
      expect(attached?.baseUrl).toBe(baseUrl);
      await fs.promises.writeFile(path.join(root, SESSION_FILE), sessionDocument(baseUrl, "c".repeat(32)), { mode: 0o600 });
      expect(await tryAttach(root, fetch)).toBeNull();
    } finally {
      await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
    }
  });

  it("retires only a leftover service process", async () => {
    const root = tempDir();
    const pidPath = path.join(root, SERVICE_PID_FILE);
    await fs.promises.writeFile(pidPath, servicePidDocument(77));
    const signals: number[] = [];
    const control: ProcessControl = {
      signal(pid) { signals.push(pid); },
      commandLine: (pid) => pid === 77 ? "dotnet run --project Workbench.Service.csproj" : "",
    };
    await retireStaleService({ userRoot: root, control, selfPid: 1 });
    expect(signals).toEqual([77]);
    expect(fs.existsSync(pidPath)).toBe(false);

    await fs.promises.writeFile(pidPath, servicePidDocument(88));
    await retireStaleService({
      userRoot: root,
      selfPid: 2,
      control: { signal(pid) { signals.push(pid); }, commandLine: () => "Workbench.App.dll" },
    });
    expect(signals).toEqual([77]);
    expect(fs.existsSync(pidPath)).toBe(true);

    await fs.promises.writeFile(pidPath, servicePidDocument(89));
    await retireStaleService({
      userRoot: root,
      selfPid: 2,
      control: { signal(pid) { signals.push(pid); }, commandLine: () => "" },
    });
    expect(fs.existsSync(pidPath)).toBe(false);
  });

  it("does not retire the current process or a pid file that is not a service", async () => {
    const root = tempDir();
    const signals: number[] = [];
    await fs.promises.writeFile(path.join(root, SERVICE_PID_FILE), servicePidDocument(process.pid));
    await retireStaleService({
      userRoot: root,
      selfPid: process.pid,
      control: { signal(pid) { signals.push(pid); }, commandLine: () => "dotnet Workbench.Service.dll" },
    });
    expect(signals).toEqual([]);
    await fs.promises.writeFile(path.join(root, SERVICE_PID_FILE), "not-json");
    await retireStaleService({
      userRoot: root,
      selfPid: 10,
      control: { signal(pid) { signals.push(pid); }, commandLine: () => "dotnet Workbench.Service.dll" },
    });
    expect(signals).toEqual([]);
    expect(assertNoLan(["--print-token"])).toBeUndefined();
    expect(() => assertNoLan(["--allow-lan"])).toThrow(ShellRejected);
  });

  it("reports a private session write failure and a start timeout without the token", async () => {
    const root = tempDir();
    const blocked = path.join(root, "not-a-directory");
    await fs.promises.writeFile(blocked, "file");
    const fake = fakeService([`listening: http://127.0.0.1:5152`, `session-token: ${token}`]);
    await expect(attachOrStart({
      repoRoot: root,
      projectPath: "Workbench.Service.csproj",
      userRoot: blocked,
      attach: false,
      dotnetPath: "dotnet",
      spawnImpl: fake.spawn,
      control: fake.control,
    })).rejects.toThrow("session-file-failed");
    expect(fake.signals).toContain(424242);

    const previous = process.env.WORKBENCH_SERVICE_START_TIMEOUT_MS;
    process.env.WORKBENCH_SERVICE_START_TIMEOUT_MS = "1000";
    const stdout = new PassThrough();
    const stderr = new PassThrough();
    const hanging: ServiceChild = { pid: 101, stdout, stderr, once() { return undefined; } };
    try {
      await expect(attachOrStart({
        repoRoot: root,
        projectPath: "Workbench.Service.csproj",
        userRoot: root,
        attach: false,
        dotnetPath: "dotnet",
        spawnImpl: () => hanging,
        control: { signal() { return undefined; }, commandLine: () => "" },
      })).rejects.toThrow("service-start-timeout");
    } finally {
      if (previous === undefined) delete process.env.WORKBENCH_SERVICE_START_TIMEOUT_MS;
      else process.env.WORKBENCH_SERVICE_START_TIMEOUT_MS = previous;
    }
  });

  it("ignores a missing session file and a health payload that is not loopback", async () => {
    const root = tempDir();
    expect(await tryAttach(root, fetch)).toBeNull();
    await fs.promises.writeFile(path.join(root, SESSION_FILE), "{", "utf8");
    expect(await tryAttach(root, fetch)).toBeNull();
    const server = http.createServer((_request, response) => {
      response.writeHead(200, { "content-type": "application/json" });
      response.end(JSON.stringify({ status: "ok", bind: "lan", apiVersion: "2.0.0-preview.1" }));
    });
    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
    const address = server.address();
    if (!address || typeof address === "string") throw new Error("test-port-missing");
    await fs.promises.writeFile(path.join(root, SESSION_FILE), sessionDocument(`http://127.0.0.1:${address.port}`, token));
    try {
      expect(await tryAttach(root, fetch)).toBeNull();
      expect(await tryAttach(root, () => Promise.reject(new Error("down")))).toBeNull();
    } finally {
      await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
    }
  });

  it("reads the current command line and does not signal a missing process", () => {
    const control = hostProcessControl(process.platform);
    expect(control.commandLine(process.pid).length).toBeGreaterThan(0);
    expect(control.commandLine(0)).toBe("");
    expect(() => control.signal(2_147_483_646, "SIGTERM", false)).not.toThrow();
  });
});
