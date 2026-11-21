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

async function theme(page: Page, name: "浅色" | "深色") {
  await page.goto("/settings");
  await page.getByRole("radio", { name: new RegExp(name) }).click();
  await expect(page.getByRole("radio", { name: new RegExp(name) })).toBeChecked();
}

test("browse, detail, and updates only describe plugins on this computer", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await theme(page, "浅色");
  await page.goto("/market");
  await expect(page.getByRole("heading", { name: "浏览" })).toBeVisible();
  await expect(page.getByText("这里只列出这台电脑上读到的插件。没有网上目录，也不能从这里安装。")).toBeVisible();
  await expect(page.getByRole("link", { name: /UPCAM 站会日报/ })).toBeVisible();
  await expect(page.getByRole("link", { name: /工作摘要/ })).toBeVisible();
  await expect(page.getByText("会议纪要助手")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "安装" })).toHaveCount(0);
  await page.screenshot({ path: "e2e/screenshots/market-browse-1100-light.png", fullPage: true });
  await page.getByRole("link", { name: /UPCAM 站会日报/ }).click();
  await expect(page.getByRole("heading", { name: "UPCAM 站会日报" })).toBeVisible();
  await expect(page.getByText("0.1.0")).toBeVisible();
  await expect(page.getByText("这台电脑", { exact: true })).toBeVisible();
  await expect(page.getByText("这是这台电脑上读到的记录。没有网上的介绍，也不能从这里安装或卸掉。")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/market-detail-1100-light.png", fullPage: true });
  await page.goto("/market/minutes");
  await expect(page.getByRole("heading", { name: "这台电脑上没有这个插件" })).toBeVisible();
  await expect(page.getByText("没有去网上查，也不能从这里安装。")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/market-missing-1100-light.png", fullPage: true });
  await page.goto("/market/updates");
  await expect(page.getByText("没有可下载的更新。下面是这台电脑上的版本，不会从网上下载，也不能在这里卸掉。")).toBeVisible();
  await expect(page.getByText("本机版本 0.1.0").first()).toBeVisible();
  await expect(page.getByRole("button", { name: "更新全部" })).toHaveCount(0);
  await page.screenshot({ path: "e2e/screenshots/market-updates-1100-light.png", fullPage: true });

  await theme(page, "深色");
  await page.setViewportSize({ width: 1100, height: 800 });
  await page.goto("/market");
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page.screenshot({ path: "e2e/screenshots/market-browse-1100-dark.png", fullPage: true });
  await page.getByRole("link", { name: /UPCAM 站会日报/ }).click();
  await expect(page.getByRole("heading", { name: "UPCAM 站会日报" })).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/market-detail-1100-dark.png", fullPage: true });
  await page.goto("/market/updates");
  await page.screenshot({ path: "e2e/screenshots/market-updates-1100-dark.png", fullPage: true });
  await page.goto("/market/minutes");
  await expect(page.getByRole("heading", { name: "这台电脑上没有这个插件" })).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/market-missing-1100-dark.png", fullPage: true });
});
