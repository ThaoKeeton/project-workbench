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
  await page.getByRole("radio", { name: new RegExp(name) }).click();
  await expect(page.getByRole("radio", { name: new RegExp(name) })).toBeChecked();
}

async function shot(page: Page, name: string) {
  await page.screenshot({ path: `e2e/screenshots/${name}.png`, fullPage: true });
}

test("project switch updates the current project", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/");
  await expect(page.getByRole("button", { name: /搜索插件、项目、设备或命令/ })).toBeVisible();
  await page.keyboard.press("Control+k");
  await page.getByRole("option", { name: "灯塔实验" }).click();
  await expect(page.locator(".context-project")).toContainText("灯塔实验");
});

test("sample cases stay inside the test design plugin", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await chooseTheme(page, "深色");
  await page.getByRole("button", { name: "测试设计" }).click();
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
  await page.getByRole("radio", { name: /跟随系统/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page.emulateMedia({ colorScheme: "light" });
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
});

for (const [theme, label] of [["dark", "深色"], ["light", "浅色"]] as const) {
  test.describe(theme, () => {
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 });
      await chooseTheme(page, label);
    });

    test("home, palette, and device menu", async ({ page }) => {
      await page.goto("/");
      await expect(page.getByRole("button", { name: /搜索插件、项目、设备或命令/ })).toBeVisible();
      await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
      await shot(page, `home-${theme}`);
      const axe = await new AxeBuilder({ page }).withTags(["wcag2aa"]).analyze();
      const serious = axe.violations.filter((item) => item.impact === "serious" || item.impact === "critical");
      expect(serious, serious.map((item) => item.id).join(",")).toEqual([]);
      await page.keyboard.press("Control+k");
      await expect(page.getByRole("dialog", { name: "命令条" })).toBeVisible();
      await shot(page, `command-${theme}`);
      await page.keyboard.press("Escape");
      await page.getByRole("button", { name: /星河笔记本/ }).click();
      await expect(page.getByRole("menuitem", { name: /书房工作站/ })).toBeVisible();
      await shot(page, `devices-menu-${theme}`);
    });

    test("market, installed, updates, and import", async ({ page }) => {
      await page.goto("/market");
      await expect(page.getByRole("heading", { name: "插件市场" })).toBeVisible();
      await expect(page.getByRole("button", { name: "安装" }).first()).toBeVisible();
      await shot(page, `market-${theme}`);
      await page.getByRole("link", { name: "已安装" }).click();
      await expect(page.getByRole("heading", { name: "已安装" })).toBeVisible();
      await shot(page, `installed-${theme}`);
      await page.getByRole("link", { name: "更新" }).click();
      await expect(page.getByText("即将安装")).toBeVisible();
      await shot(page, `updates-${theme}`);
      await page.getByRole("link", { name: "本地导入" }).click();
      await expect(page.getByText("未请求网络访问")).toBeVisible();
      await shot(page, `import-${theme}`);
    });

    test("plugin, diagnostics, settings, pairing, offline, and crash", async ({ page }) => {
      await page.goto("/plugin/upcam");
      await expect(page.getByRole("heading", { name: "站会日报" })).toBeVisible();
      await expect(page.getByText("自动发送已关闭")).toBeVisible();
      await shot(page, `plugin-${theme}`);
      await page.goto("/diagnostics");
      await expect(page.getByText("仅本机回环")).toBeVisible();
      await expect(page.getByText("自动发送：关闭")).toBeVisible();
      await expect(page.getByText("1,100")).toBeVisible();
      await shot(page, `diagnostics-${theme}`);
      await page.goto("/settings");
      await expect(page.getByRole("radio", { name: new RegExp(label) })).toBeChecked();
      await shot(page, `settings-${theme}`);
      await page.goto("/devices");
      await expect(page.getByText("7K4M — XQ2P")).toBeVisible();
      await shot(page, `pairing-${theme}`);
      await page.goto("/offline");
      await expect(page.getByRole("heading", { name: "本地服务未连接" })).toBeVisible();
      await shot(page, `offline-${theme}`);
      await page.goto("/plugin/test/stopped");
      await expect(page.getByText("exit 1 · renderer timeout after 12s")).toBeVisible();
      await shot(page, `crashed-${theme}`);
    });
  });
}

test.describe("minimum width", () => {
  test.use({ viewport: { width: 1100, height: 900 } });

  test("keeps the icon rail at 1100px", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("navigation", { name: "主导航" })).toBeVisible();
    await expect(page.getByRole("button", { name: /星河笔记本/ })).toBeVisible();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);
    await page.screenshot({ path: "e2e/screenshots/home-1100.png", fullPage: true });
  });
});
