import type { InsightState } from "../../../plugins/v2/Workbench.DingTalkInsights.V2/ui/DingPanel";
import type { Dashboard } from "../../../plugins/v2/Workbench.TestingDesign.V2/ui/TestingPanel";
import type { Digest, NoteFile } from "../../../plugins/v2/Workbench.UpcamStandup.V2/ui/UpcamPanel";

export type Info = {
  product: string;
  apiVersion: string;
  preview: boolean;
  device: { id: string; label: string; kind: string };
  lan: { enabled: boolean; availableInPreview: boolean };
  autoSend: boolean;
  catalogSchemaVersion: number;
};

export type ProjectItem = { id: string; name: string; isArchived: boolean };

export type Projects = {
  schemaVersion: number;
  revision: string;
  activeProjectId: string;
  projects: ProjectItem[];
};

export type PluginItem = {
  id: string;
  name: string;
  version: string;
  contract: string;
  state: string;
  trusted: boolean;
  loaded: boolean;
  diagnostics: { code: string }[];
  permissions: { id: string; sensitive: boolean }[];
  uiPath: string | null;
};

export type Diagnostics = {
  schema: string;
  service: { bind: string; lanEnabled: boolean; autoSend: boolean };
  plugins: { discovered: number; ready: number; invalid: number; loaded: number };
  projects: { count: number; activePresent: boolean };
  issues: { code: string }[];
  nativeWindow: { minimumWidth: number };
};

export type { Dashboard, Digest, InsightState, NoteFile };

export type CaseRow = { id: string; action: string; expected: string };

export type CaseDocument = {
  schema: string;
  schemaVersion: number;
  suiteName: string;
  rows: CaseRow[];
};

const sampleId = "com.projectworkbench.test-cases.v2";
const upcamId = "com.projectworkbench.upcam-standup.v2";
const dingId = "com.projectworkbench.dingtalk-insights.v2";
const testingId = "com.projectworkbench.testing-design.v2";

export class ApiClient {
  constructor(private readonly token: string) {}

  get tokenPresent(): boolean {
    return this.token.length > 0;
  }

  info(): Promise<Info> {
    return this.json("/api/v2/info");
  }

  projects(): Promise<Projects> {
    return this.json("/api/v2/projects");
  }

  async switchProject(id: string, expectedRevision: string): Promise<Projects> {
    return this.json("/api/v2/projects/active", {
      method: "POST",
      body: JSON.stringify({ id, expectedRevision }),
    });
  }

  async plugins(): Promise<PluginItem[]> {
    const body = await this.json<{ plugins: PluginItem[] }>("/api/v2/plugins");
    return body.plugins;
  }

  diagnostics(): Promise<Diagnostics> {
    return this.json("/api/v2/diagnostics");
  }

  cases(): Promise<CaseDocument> {
    return this.json(`/api/v2/plugins/${sampleId}/cases`);
  }

  saveCases(document: CaseDocument): Promise<CaseDocument> {
    return this.json(`/api/v2/plugins/${sampleId}/cases`, {
      method: "PUT",
      body: JSON.stringify(document),
    });
  }

  async exportCases(): Promise<Blob> {
    const response = await this.send(`/api/v2/plugins/${sampleId}/export`, { method: "POST" });
    return response.blob();
  }

  upcamDraft(): Promise<Digest> {
    return this.json(`/api/v2/plugins/${upcamId}/actions/draft`);
  }

  upcamImport(files: NoteFile[]): Promise<Digest> {
    return this.json(`/api/v2/plugins/${upcamId}/actions/import`, { method: "POST", body: JSON.stringify({ files }) });
  }

  upcamSample(): Promise<Digest> {
    return this.json(`/api/v2/plugins/${upcamId}/actions/sample`, { method: "POST", body: "{}" });
  }

  dingState(): Promise<InsightState> {
    return this.json(`/api/v2/plugins/${dingId}/actions/state`);
  }

  dingImport(text: string): Promise<InsightState> {
    return this.json(`/api/v2/plugins/${dingId}/actions/import`, { method: "POST", body: JSON.stringify({ text }) });
  }

  dingSample(): Promise<InsightState> {
    return this.json(`/api/v2/plugins/${dingId}/actions/sample`, { method: "POST", body: "{}" });
  }

  dingAnalyze(): Promise<InsightState> {
    return this.json(`/api/v2/plugins/${dingId}/actions/analyze`, { method: "POST", body: "{}" });
  }

  testingDashboard(): Promise<Dashboard> {
    return this.json(`/api/v2/plugins/${testingId}/actions/dashboard`);
  }

  async openSession(): Promise<boolean> {
    const response = await this.send("/api/v2/session", { method: "POST" });
    return response.ok;
  }

  async theme(): Promise<"system" | "light" | "dark"> {
    const body = await this.json<{ setting: string }>("/api/v2/ui/theme");
    return body.setting === "light" || body.setting === "dark" ? body.setting : "system";
  }

  async saveTheme(setting: "system" | "light" | "dark"): Promise<void> {
    await this.json("/api/v2/ui/theme", { method: "PUT", body: JSON.stringify({ setting }) });
  }

  private async json<T>(path: string, init?: RequestInit): Promise<T> {
    const response = await this.send(path, init);
    return (await response.json()) as T;
  }

  private async send(path: string, init?: RequestInit): Promise<Response> {
    const headers = new Headers(init?.headers);
    headers.set("Authorization", `Bearer ${this.token}`);
    if (init?.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
    const response = await fetch(path, { ...init, headers, credentials: "include" });
    if (!response.ok) {
      let code = String(response.status);
      try {
        const body = (await response.json()) as { error?: string };
        if (body.error) code = body.error;
      } catch {
        code = String(response.status);
      }
      throw new Error(code);
    }
    return response;
  }
}

export function readSessionToken(): string {
  return sessionStorage.getItem("workbench.session") ?? "";
}
