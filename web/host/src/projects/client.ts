export type ProjectItem = { id: string; name: string; isArchived: boolean };

export type ProjectRoster = {
  schemaVersion: number;
  revision: string;
  activeProjectId: string;
  projects: ProjectItem[];
};

export type CreatedProject = ProjectRoster & { opened: boolean };

export type ProjectNote = {
  schemaVersion: number;
  revision: string;
  text: string;
  missing: boolean;
};

export type ProjectDeskApi = {
  list(): Promise<ProjectRoster>;
  create(name: string, expectedRevision: string): Promise<CreatedProject>;
  rename(name: string, expectedRevision: string): Promise<ProjectRoster>;
  open(id: string, expectedRevision: string): Promise<ProjectRoster>;
  readNote(): Promise<ProjectNote>;
  saveNote(text: string, expectedRevision: string, projectId: string): Promise<ProjectNote>;
};

export class ProjectDeskClient implements ProjectDeskApi {
  constructor(private readonly token: string) {}

  list(): Promise<ProjectRoster> {
    return this.json("/api/v2/projects");
  }

  async create(name: string, expectedRevision: string): Promise<CreatedProject> {
    const body = await this.json<CreatedProject>("/api/v2/projects", {
      method: "POST",
      body: JSON.stringify({ name, expectedRevision }),
    });
    if (typeof body.opened !== "boolean" || body.projects === undefined) throw new Error("ServiceFailed");
    return body;
  }

  rename(name: string, expectedRevision: string): Promise<ProjectRoster> {
    return this.json("/api/v2/projects/name", {
      method: "POST",
      body: JSON.stringify({ name, expectedRevision }),
    });
  }

  open(id: string, expectedRevision: string): Promise<ProjectRoster> {
    return this.json("/api/v2/projects/active", {
      method: "POST",
      body: JSON.stringify({ id, expectedRevision }),
    });
  }

  readNote(): Promise<ProjectNote> {
    return this.json("/api/v2/projects/active/context");
  }

  saveNote(text: string, expectedRevision: string, projectId: string): Promise<ProjectNote> {
    return this.json("/api/v2/projects/active/context", {
      method: "PUT",
      body: JSON.stringify({ text, expectedRevision, projectId }),
    });
  }

  private async json<T>(path: string, init?: RequestInit): Promise<T> {
    const headers = new Headers(init?.headers);
    headers.set("Authorization", `Bearer ${this.token}`);
    if (init?.body) headers.set("Content-Type", "application/json");
    const response = await fetch(path, { ...init, headers, credentials: "include" });
    if (!response.ok) throw new Error(await errorCode(response));
    return (await response.json()) as T;
  }
}

async function errorCode(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { error?: string };
    if (body.error) return body.error;
  } catch {
    return "ServiceFailed";
  }
  return "ServiceFailed";
}
