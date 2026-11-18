import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page, type Route } from "@playwright/test";

type Project = { id: string; name: string; isArchived: boolean };
type Note = { revision: string; text: string };

type DeskState = {
  revision: string;
  generation: number;
  activeProjectId: string;
  projects: Project[];
  notes: Record<string, Note>;
};

function fresh(): DeskState {
  return {
    revision: "rev-1",
    generation: 1,
    activeProjectId: "default",
    projects: [
      { id: "default", name: "星河协作", isArchived: false },
      { id: "lamp", name: "灯塔实验", isArchived: false },
    ],
    notes: {
      default: { revision: "note-1", text: "只在星河" },
      lamp: { revision: "note-lamp", text: "只在灯塔" },
    },
  };
}

let state = fresh();

function sameName(left: string, right: string): boolean {
  return left.toLocaleLowerCase("en-US") === right.toLocaleLowerCase("en-US");
}

function nameProblem(name: string, exceptId?: string): string | null {
  const trimmed = name.trim();
  if (trimmed.length === 0 || /[\u0000-\u001f\u007f-\u009f]/.test(name)) return "InvalidName";
  if (trimmed.length > 80) return "InvalidName";
  const taken = state.projects.find((project) => project.id !== exceptId && sameName(project.name, trimmed));
  if (taken) return "DuplicateName";
  return null;
}

function roster(opened?: boolean) {
  return {
    schemaVersion: 1,
    revision: state.revision,
    activeProjectId: state.activeProjectId,
    projects: state.projects,
    ...(opened === undefined ? {} : { opened }),
  };
}

function bump() {
  state.generation += 1;
  state.revision = `rev-${state.generation}`;
}

function noteBody() {
  const saved = state.notes[state.activeProjectId];
  if (!saved) return { schemaVersion: 1, revision: "missing", text: "", missing: true };
  return { schemaVersion: 1, revision: saved.revision, text: saved.text, missing: false };
}

async function fulfill(route: Route, status: number, body: unknown) {
  await route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
}

async function handle(route: Route) {
  const request = route.request();
  const path = new URL(request.url()).pathname;
  const method = request.method();
  const body = method === "GET" ? {} : JSON.parse(request.postData() || "{}") as { name?: string; id?: string; text?: string; expectedRevision?: string };
  if (method === "GET" && path === "/api/v2/projects") return fulfill(route, 200, roster());
  if (method === "GET" && path === "/api/v2/projects/active/context") return fulfill(route, 200, noteBody());
  if (body.expectedRevision && body.expectedRevision !== state.revision && path !== "/api/v2/projects/active/context") {
    return fulfill(route, 409, { error: "RevisionConflict" });
  }
  if (method === "POST" && path === "/api/v2/projects") {
    const problem = nameProblem(body.name ?? "");
    if (problem) return fulfill(route, problem === "DuplicateName" ? 409 : 400, { error: problem });
    const id = `p${state.generation}`;
    state.projects = [...state.projects, { id, name: (body.name ?? "").trim(), isArchived: false }];
    state.notes[id] = { revision: "missing", text: "" };
    state.activeProjectId = id;
    bump();
    return fulfill(route, 200, roster(true));
  }
  if (method === "POST" && path === "/api/v2/projects/name") {
    const problem = nameProblem(body.name ?? "", state.activeProjectId);
    if (problem) return fulfill(route, problem === "DuplicateName" ? 409 : 400, { error: problem });
    const trimmed = (body.name ?? "").trim();
    const current = state.projects.find((project) => project.id === state.activeProjectId);
    if (current && current.name !== trimmed) {
      current.name = trimmed;
      bump();
    }
    return fulfill(route, 200, roster());
  }
  if (method === "POST" && path === "/api/v2/projects/active") {
    const target = state.projects.find((project) => project.id === body.id);
    if (!target) return fulfill(route, 404, { error: "ProjectNotFound" });
    state.activeProjectId = target.id;
    bump();
    return fulfill(route, 200, roster());
  }
  if (method === "PUT" && path === "/api/v2/projects/active/context") {
    const saved = state.notes[state.activeProjectId] ?? { revision: "missing", text: "" };
    if ((body.expectedRevision ?? "") !== saved.revision) return fulfill(route, 409, { error: "RevisionConflict" });
    const next = { revision: `note-${state.generation + 1}`, text: body.text ?? "" };
    state.notes[state.activeProjectId] = next;
    return fulfill(route, 200, { schemaVersion: 1, revision: next.revision, text: next.text, missing: false });
  }
  return fulfill(route, 404, { error: "NotFound" });
}

async function shot(page: Page, name: string) {
  await page.screenshot({ path: `e2e/screenshots/${name}.png`, fullPage: true });
}

test.beforeEach(async ({ page }) => {
  state = fresh();
  await page.addInitScript(() => {
    sessionStorage.setItem("workbench.session", "preview-session");
    if (!localStorage.getItem("workbench.theme")) localStorage.setItem("workbench.theme", "light");
  });
  await page.route(/\/api\/v2\/projects/, handle);
});

test("create, rename, isolation, and a failed switch stay on the current project", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await page.goto("/project-desk.html");
  await expect(page.getByRole("heading", { name: "星河协作" })).toBeVisible();
  await expect(page.getByLabel(/只保存在/)).toHaveValue("只在星河");
  await shot(page, "project-desk-1100-light");

  await page.getByRole("button", { name: "新建项目" }).click();
  await page.getByLabel("项目名称").fill("星河协作");
  await expect(page.getByRole("alert")).toHaveText("已经有叫「星河协作」的项目。");
  await shot(page, "project-desk-name-1100-light");
  await page.getByRole("button", { name: "创建并打开" }).click();
  await expect(page.getByRole("heading", { name: "星河协作" })).toBeVisible();
  await page.keyboard.press("Escape");

  await page.getByRole("button", { name: "新建项目" }).click();
  await page.getByLabel("项目名称").fill("   ");
  await page.getByRole("button", { name: "创建并打开" }).click();
  await expect(page.getByRole("alert")).toHaveText("请先写一个项目名称。");
  await page.keyboard.press("Escape");

  await page.getByRole("button", { name: "新建项目" }).click();
  await page.getByLabel("项目名称").fill("海岸记录");
  await page.getByRole("button", { name: "创建并打开" }).click();
  await expect(page.getByRole("heading", { name: "海岸记录" })).toBeVisible();
  await expect(page.getByLabel(/只保存在「海岸记录」/)).toHaveValue("");
  await page.getByLabel(/只保存在「海岸记录」/).fill("只在海岸");
  await page.getByRole("heading", { name: "这台电脑上的项目" }).click();
  await expect(page.getByText("已保存在这个项目。")).toBeVisible();

  await page.getByRole("button", { name: "打开「星河协作」" }).click();
  await expect(page.getByRole("heading", { name: "星河协作" })).toBeVisible();
  await expect(page.getByLabel(/只保存在「星河协作」/)).toHaveValue("只在星河");
  await page.getByRole("button", { name: "打开「海岸记录」" }).click();
  await expect(page.getByLabel(/只保存在「海岸记录」/)).toHaveValue("只在海岸");

  await page.route("**/api/v2/projects/active", async (route) => {
    if (route.request().method() === "POST") {
      await route.fulfill({ status: 404, contentType: "application/json", body: JSON.stringify({ error: "ProjectNotFound" }) });
      return;
    }
    await handle(route);
  });
  await page.getByRole("button", { name: "打开「灯塔实验」" }).click();
  await expect(page.getByRole("status").filter({ hasText: "没能换到「灯塔实验」" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "海岸记录" })).toBeVisible();
  await expect(page.getByLabel(/只保存在「海岸记录」/)).toHaveValue("只在海岸");
  await page.unroute("**/api/v2/projects/active");

  await page.getByRole("button", { name: "重命名" }).click();
  const rename = page.getByRole("dialog", { name: "重命名当前项目" });
  await rename.getByLabel("项目名称").fill("灯塔实验");
  await expect(rename.getByRole("alert")).toHaveText("已经有叫「灯塔实验」的项目。");
  await rename.getByLabel("项目名称").fill("北港记录");
  await rename.getByRole("button", { name: "重命名" }).click();
  await expect(page.getByRole("heading", { name: "北港记录" })).toBeVisible();
  await expect(page.getByLabel(/只保存在「北港记录」/)).toHaveValue("只在海岸");

  await page.reload();
  await expect(page.getByRole("heading", { name: "北港记录" })).toBeVisible();
  await expect(page.getByLabel(/只保存在「北港记录」/)).toHaveValue("只在海岸");

  await page.evaluate(() => localStorage.setItem("workbench.theme", "dark"));
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await expect(page.getByRole("heading", { name: "北港记录" })).toBeVisible();
  await shot(page, "project-desk-1100-dark");
  const axe = await new AxeBuilder({ page }).withTags(["wcag2aa"]).analyze();
  const serious = axe.violations.filter((item) => item.impact === "serious" || item.impact === "critical");
  expect(serious, serious.map((item) => item.id).join(",")).toEqual([]);
});
