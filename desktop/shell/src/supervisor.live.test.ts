import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { findRepoRoot, resolveDotnet, resolveUiDist, sessionDocument } from "./protocol";
import { attachOrStart } from "./supervisor";

const live = process.env.WORKBENCH_SHELL_LIVE === "1";

describe("real loopback service", () => {
  it.skipIf(!live)("starts the .NET service, serves the React UI, and stops", async () => {
    const repoRoot = findRepoRoot(process.cwd());
    const uiDist = resolveUiDist(undefined, repoRoot);
    const userRoot = fs.mkdtempSync(path.join(os.tmpdir(), "wb-v2-live-"));
    const running = await attachOrStart({
      repoRoot,
      projectPath: path.join(repoRoot, "src", "Workbench.Service", "Workbench.Service.csproj"),
      userRoot,
      uiDist,
      attach: false,
      dotnetPath: resolveDotnet(process.env, process.platform),
    });
    try {
      if (running.baseUrl.startsWith("http://127.0.0.1:") === false || running.owned !== true) {
        throw new Error("service-not-loopback");
      }
      const headers = { Authorization: `Bearer ${running.token}` };
      const info = await fetch(new URL("/api/v2/info", running.baseUrl), { headers }).then((response) => response.json()) as {
        apiVersion: string;
        autoSend: boolean;
        lan: { enabled: boolean };
        catalogSchemaVersion: number;
        preview: boolean;
        host: string;
      };
      expect(info).toMatchObject({
        apiVersion: "2.0.0-preview.1",
        autoSend: false,
        lan: { enabled: false },
        catalogSchemaVersion: 1,
        preview: true,
        host: "local-service",
      });
      const plugins = await fetch(new URL("/api/v2/plugins", running.baseUrl), { headers }).then((response) => response.json()) as {
        plugins: Array<{ id: string; loaded: boolean; contract: string; permissions: Array<{ id: string; sensitive: boolean }> }>;
      };
      const sample = plugins.plugins.find((plugin) => plugin.id === "com.projectworkbench.test-cases.v2");
      expect(sample).toMatchObject({ loaded: true, contract: "v2-preview" });
      expect(sample?.permissions.some((permission) => permission.id === "message.send" || permission.sensitive)).toBe(false);
      const page = await fetch(new URL("/", running.baseUrl)).then((response) => response.text());
      expect(page).toContain('id="root"');
      const stored = fs.readFileSync(path.join(userRoot, "v2-session.json"), "utf8");
      if (stored !== sessionDocument(running.baseUrl, running.token)) throw new Error("session-file-mismatch");
      const official = path.join(process.env.LOCALAPPDATA ?? path.join(os.homedir(), ".local", "share"), "ProjectWorkbench");
      expect(path.resolve(userRoot).startsWith(path.resolve(official) + path.sep)).toBe(false);
    } finally {
      await running.stop();
      fs.rmSync(userRoot, { recursive: true, force: true });
    }
    await expect(fetch(new URL("/api/v2/health", running.baseUrl), { signal: AbortSignal.timeout(1000) })).rejects.toThrow();
  }, 300_000);
});
