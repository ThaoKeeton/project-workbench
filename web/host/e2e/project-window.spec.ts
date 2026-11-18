import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

test.beforeEach(async ({ page, request }) => {
  await request.post("http://127.0.0.1:4317/api/v2/__reset");
  await page.addInitScript(() => {
    sessionStorage.setItem("workbench.session", "preview-session");
    if (!sessionStorage.getItem("workbench.theme-ready")) {
      localStorage.removeItem("workbench.theme");
      sessionStorage.setItem("workbench.theme-ready", "1");
    }
  });
});

async function chooseTheme(page: Page, name: "深色" | "浅色") {
  await page.goto("/settings");
  await page.getByRole("radio", { name: new RegExp(name) }).click();
  await expect(page.getByRole("radio", { name: new RegExp(name) })).toBeChecked();
}

async function shot(page: Page, name: string) {
  await page.screenshot({ path: `e2e/screenshots/${name}.png`, fullPage: true });
}

test("the top bar opens project management inside the main window", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await chooseTheme(page, "浅色");
  await page.goto("/");
  await expect(page.getByRole("button", { name: /当前项目 星河协作/ })).toBeVisible();
  await page.getByRole("button", { name: /当前项目/ }).click();
  await page.getByRole("menuitem", { name: "新建或改名" }).click();
  await expect(page.getByRole("heading", { name: "项目", exact: true })).toBeVisible();
  await expect(page.getByRole("navigation", { name: "主导航" })).toBeVisible();
  await expect(page.getByLabel(/只保存在「星河协作」/)).toHaveValue("只在星河");
  await shot(page, "project-window-1100-light");

  await page.getByRole("button", { name: "新建项目" }).click();
  await page.getByLabel("项目名称").fill("星河协作");
  await expect(page.getByRole("alert")).toHaveText("已经有叫「星河协作」的项目。");
  await expect(page.getByRole("button", { name: /当前项目 星河协作/ })).toBeVisible();
  await expect(page.getByLabel(/只保存在「星河协作」/)).toHaveValue("只在星河");
  await page.keyboard.press("Escape");

  await page.getByRole("button", { name: "新建项目" }).click();
  await page.getByRole("dialog", { name: "新建项目" }).getByLabel("项目名称").fill("海岸记录");
  await page.getByRole("button", { name: "创建并打开" }).click();
  await expect(page.getByRole("button", { name: /当前项目 海岸记录/ })).toBeVisible();
  await expect(page.getByLabel(/只保存在「海岸记录」/)).toHaveValue("");

  await page.getByRole("button", { name: "打开「星河协作」" }).click();
  await expect(page.getByRole("button", { name: /当前项目 星河协作/ })).toBeVisible();
  await expect(page.getByLabel(/只保存在「星河协作」/)).toHaveValue("只在星河");
  await page.getByRole("button", { name: "打开「海岸记录」" }).click();
  await expect(page.getByLabel(/只保存在「海岸记录」/)).toHaveValue("");
  await page.getByLabel(/只保存在「海岸记录」/).fill("只在海岸");
  await page.getByRole("heading", { name: "这台电脑上的项目" }).click();
  await expect(page.getByText("已保存在这个项目。")).toBeVisible();

  await page.getByRole("button", { name: "重命名" }).click();
  const rename = page.getByRole("dialog", { name: "重命名当前项目" });
  await rename.getByLabel("项目名称").fill("北港记录");
  await rename.getByRole("button", { name: "重命名" }).click();
  await expect(page.getByRole("button", { name: /当前项目 北港记录/ })).toBeVisible();
  await expect(page.getByLabel(/只保存在「北港记录」/)).toHaveValue("只在海岸");

  await page.getByRole("button", { name: "返回首页" }).click();
  await expect(page.getByRole("button", { name: /搜索插件、项目、设备或命令/ })).toBeVisible();
  await expect(page.getByRole("button", { name: /当前项目 北港记录/ })).toBeVisible();
  await page.reload();
  await expect(page.getByRole("button", { name: /当前项目 北港记录/ })).toBeVisible();
  await page.getByRole("button", { name: /当前项目/ }).click();
  await page.getByRole("menuitem", { name: "新建或改名" }).click();
  await expect(page.getByLabel(/只保存在「北港记录」/)).toHaveValue("只在海岸");
  await expect(page.getByLabel(/只保存在「星河协作」/)).toHaveCount(0);

  await chooseTheme(page, "深色");
  await page.setViewportSize({ width: 1100, height: 800 });
  await page.goto("/projects");
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await expect(page.getByRole("heading", { name: "项目", exact: true })).toBeVisible();
  await shot(page, "project-window-1100-dark");
  const axe = await new AxeBuilder({ page }).withTags(["wcag2aa"]).analyze();
  const serious = axe.violations.filter((item) => item.impact === "serious" || item.impact === "critical");
  expect(serious, serious.map((item) => item.id).join(",")).toEqual([]);
});

test("a failed switch on the project page keeps the note", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await page.goto("/projects");
  await expect(page.getByLabel(/只保存在「星河协作」/)).toHaveValue("只在星河");
  await page.route("**/api/v2/projects/active", async (route) => {
    if (route.request().method() === "POST") {
      await route.fulfill({ status: 409, contentType: "application/json", body: JSON.stringify({ error: "RevisionConflict" }) });
      return;
    }
    await route.continue();
  });
  await page.getByRole("button", { name: /当前项目/ }).click();
  await page.getByRole("menuitem", { name: "灯塔实验" }).click();
  await expect(page.getByRole("status").filter({ hasText: "请再选一次" })).toBeVisible();
  await expect(page.getByRole("button", { name: /当前项目 星河协作/ })).toBeVisible();
  await expect(page.getByLabel(/只保存在「星河协作」/)).toHaveValue("只在星河");
});
