import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page, request }) => {
  await request.post("http://127.0.0.1:4317/api/v2/__reset");
  await page.addInitScript(() => sessionStorage.setItem("workbench.session", "preview-session"));
});

test("overview shows the local device and stays on loopback", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByText("当前设备：本机")).toBeVisible();
  await expect(page.getByRole("heading", { name: "本机概览" })).toBeVisible();
  await expect(page.getByText("当前项目：默认示例")).toBeVisible();
  await expect(page.getByText("自动发送：关闭")).toBeVisible();
  await expect(page.getByText("局域网未开放")).toBeVisible();
  await expect(page.getByText("实时通道：已连接")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/overview.png", fullPage: true });
});

test("project switch updates the current project", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("link", { name: "项目" }).click();
  await expect(page.getByRole("heading", { name: "示例项目" })).toBeVisible();
  await page.getByRole("button", { name: "切换到此项目" }).click();
  await expect(page.getByRole("status")).toHaveText("当前项目：示例项目");
  await expect(page.getByRole("banner").getByText("当前项目：示例项目")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/projects.png", fullPage: true });
});

test("plugins page embeds the sample cases", async ({ page }) => {
  await page.goto("/plugins");
  await expect(page.getByText("工作摘要 · 示例")).toBeVisible();
  await expect(page.getByText("未启用")).toBeVisible();
  await expect(page.getByText("契约 v2 预览")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/plugins.png", fullPage: true });
  await page.getByRole("button", { name: "打开插件" }).click();
  const step = page.getByRole("textbox", { name: "EX-001 步骤" });
  await expect(step).toHaveValue("启用本地插件");
  await step.fill("查看本地示例");
  await page.getByRole("button", { name: "保存示例草稿" }).click();
  await expect(page.getByRole("status")).toHaveText("已保存到本机，未发送。");
  await page.reload();
  await expect(page.getByRole("textbox", { name: "EX-001 步骤" })).toHaveValue("查看本地示例");
  await page.screenshot({ path: "e2e/screenshots/test-cases.png", fullPage: true });
});

test("diagnostics keep the native width floor visible", async ({ page }) => {
  await page.goto("/diagnostics");
  await expect(page.getByRole("main").getByText("仅本机回环")).toBeVisible();
  await expect(page.getByRole("main").getByText("自动发送：关闭")).toBeVisible();
  await expect(page.getByText("本机窗口最小宽度仍为 1,100 像素。浏览器里的界面从 640 像素起排布。")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/diagnostics.png", fullPage: true });
});

test.describe("narrow viewport", () => {
  test.use({ viewport: { width: 640, height: 900 } });

  test("lays out from 640px without a horizontal page scroll", async ({ page }) => {
    await page.goto("/");
    await expect(page.locator(".app")).toHaveAttribute("data-layout", "compact");
    await expect(page.getByText("当前设备：本机")).toBeVisible();
    await expect(page.getByRole("link", { name: "诊断" })).toBeVisible();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);
    await page.screenshot({ path: "e2e/screenshots/overview-640.png", fullPage: true });
  });
});
