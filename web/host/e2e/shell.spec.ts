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

async function chooseTheme(page: Page, name: "深色" | "浅色" | "跟随系统") {
  await page.goto("/settings");
  await page.getByRole("radio", { name }).click();
  await expect(page.getByRole("radio", { name })).toBeChecked();
}

test("project switch updates the current project", async ({ page }) => {
  await page.goto("/projects");
  await expect(page.getByRole("heading", { name: "示例项目" })).toBeVisible();
  await page.getByRole("button", { name: "切换到此项目" }).click();
  await expect(page.getByRole("status")).toHaveText("当前项目：示例项目");
  await expect(page.getByRole("navigation").getByText("示例项目")).toBeVisible();
});

test("plugins page embeds the sample cases", async ({ page }) => {
  await chooseTheme(page, "深色");
  await page.goto("/plugins");
  await expect(page.getByText("工作摘要 · 示例")).toBeVisible();
  await expect(page.getByText("未启用")).toBeVisible();
  await expect(page.getByText("契约 v2 预览")).toBeVisible();
  await page.getByRole("button", { name: "打开插件" }).click();
  const step = page.getByRole("textbox", { name: "EX-001 步骤" });
  await expect(step).toHaveValue("启用本地插件");
  await step.fill("查看本地示例");
  await page.getByRole("button", { name: "保存示例草稿" }).click();
  await expect(page.getByRole("status")).toHaveText("已保存到本机，未发送。");
  await page.reload();
  await expect(page.getByRole("textbox", { name: "EX-001 步骤" })).toHaveValue("查看本地示例");
});

test("follow system tracks the color scheme", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "dark" });
  await page.goto("/settings");
  await page.getByRole("radio", { name: "跟随系统" }).click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page.emulateMedia({ colorScheme: "light" });
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
});

for (const [theme, label] of [["dark", "深色"], ["light", "浅色"]] as const) {
  test.describe(theme, () => {
    test.beforeEach(async ({ page }) => {
      await chooseTheme(page, label);
    });

    test("overview", async ({ page }) => {
      await page.goto("/");
      await expect(page.getByRole("heading", { name: "今天的工作台" })).toBeVisible();
      await expect(page.getByRole("button", { name: "当前设备：本机" })).toBeVisible();
      await expect(page.locator(".footer-note")).toContainText("发送保持关闭");
      await expect(page.locator(".footer-note")).toContainText("局域网默认仅本机");
      await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
      await page.screenshot({ path: `e2e/screenshots/overview-${theme}.png`, fullPage: true });
      const axe = await new AxeBuilder({ page }).withTags(["wcag2aa"]).analyze();
      const serious = axe.violations.filter((item) => item.impact === "serious" || item.impact === "critical");
      expect(serious, serious.map((item) => item.id).join(",")).toEqual([]);
    });

    test("plugins", async ({ page }) => {
      await page.goto("/plugins");
      await expect(page.getByRole("heading", { name: "插件" })).toBeVisible();
      await page.screenshot({ path: `e2e/screenshots/plugins-${theme}.png`, fullPage: true });
    });

    test("diagnostics", async ({ page }) => {
      await page.goto("/diagnostics");
      await expect(page.getByText("仅本机回环")).toBeVisible();
      await expect(page.getByText("自动发送：关闭")).toBeVisible();
      await expect(page.getByText("1,100")).toBeVisible();
      await page.screenshot({ path: `e2e/screenshots/diagnostics-${theme}.png`, fullPage: true });
    });

    test("settings", async ({ page }) => {
      await page.goto("/settings");
      await expect(page.getByRole("radio", { name: label })).toBeChecked();
      await page.screenshot({ path: `e2e/screenshots/settings-${theme}.png`, fullPage: true });
    });
  });
}

test.describe("narrow viewport", () => {
  test.use({ viewport: { width: 640, height: 900 } });

  test("lays out from 640px without a horizontal page scroll", async ({ page }) => {
    await page.goto("/");
    await expect(page.locator(".app")).toHaveAttribute("data-layout", "compact");
    await expect(page.getByRole("button", { name: "当前设备：本机" })).toBeVisible();
    await expect(page.getByRole("link", { name: "诊断" })).toBeVisible();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);
    await page.screenshot({ path: "e2e/screenshots/overview-640.png", fullPage: true });
  });
});
