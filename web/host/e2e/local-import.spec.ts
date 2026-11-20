import { expect, test, type Page } from "@playwright/test";

const manifest = JSON.stringify({
  contract: "workbench.plugin.v2",
  contractVersion: "2.0.0-preview.1",
  id: "com.example.synthetic-notes",
  name: "合成备忘",
  version: "0.1.0",
  description: "只留在本机的合成清单。",
  autoSend: false,
  backend: { entryAssembly: "Notes.dll", entryType: "Example.Notes" },
  ui: { kind: "web-package", module: "./ui/Notes.tsx", mount: "embedded" },
  permissions: [{ id: "project.read", reason: "知道当前项目", sensitive: false }],
});

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

test("diagnostics and devices describe this computer only", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await theme(page, "浅色");
  await page.goto("/diagnostics");
  await expect(page.getByText("仅本机回环 · 已连接")).toBeVisible();
  await expect(page.getByText("未开放")).toBeVisible();
  await expect(page.getByText("没有。不会从网上下载插件。")).toBeVisible();
  await expect(page.getByText("局域网发现")).toBeVisible();
  await expect(page.getByText("LAN 发现")).toHaveCount(0);
  await expect(page.getByText("4 个更新可用")).toHaveCount(0);
  await page.screenshot({ path: "e2e/screenshots/diagnostics-honest-1100-light.png", fullPage: true });
  await page.goto("/devices");
  await expect(page.getByText("局域网没有开放，不能配对另一台设备，也没有配对过别的电脑。")).toBeVisible();
  await expect(page.getByText("7K4M")).toHaveCount(0);
  await page.screenshot({ path: "e2e/screenshots/devices-honest-1100-light.png", fullPage: true });
  await theme(page, "深色");
  await page.setViewportSize({ width: 1100, height: 800 });
  await page.goto("/diagnostics");
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page.screenshot({ path: "e2e/screenshots/diagnostics-honest-1100-dark.png", fullPage: true });
  await page.goto("/devices");
  await page.screenshot({ path: "e2e/screenshots/devices-honest-1100-dark.png", fullPage: true });
});

test("a chosen manifest is checked, then recorded only after confirmation", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await theme(page, "浅色");
  await page.goto("/market/import");
  await page.getByLabel("选择文件").setInputFiles({ name: "notes.plugin.v2.json", mimeType: "application/json", buffer: Buffer.from(manifest) });
  await expect(page.getByText("这是一份插件清单。")).toBeVisible();
  await expect(page.getByText("允许「project.read」：知道当前项目。不会联网。")).toBeVisible();
  await expect(page.getByText("记下后不会启用")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/import-check-1100-light.png", fullPage: true });
  await page.getByRole("button", { name: "确认记在这台电脑上" }).click();
  await expect(page.getByRole("status")).toHaveText("「合成备忘」已记在这台电脑上，还没有启用。");
  await page.screenshot({ path: "e2e/screenshots/import-saved-1100-light.png", fullPage: true });
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "这台电脑上的插件" })).toBeVisible();
  await expect(page.getByText("合成备忘")).toBeVisible();
  await expect(page.getByText("已安装，尚未启用")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/import-home-1100-light.png", fullPage: true });
  await page.reload();
  await expect(page.getByText("合成备忘")).toBeVisible();
  await page.goto("/market/installed");
  await expect(page.getByText("合成备忘")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/import-installed-1100-light.png", fullPage: true });

  await page.goto("/market/import");
  await page.getByLabel("选择文件").setInputFiles({ name: "nope.json", mimeType: "application/json", buffer: Buffer.from("{}") });
  await expect(page.getByText("这不是插件清单。请选择 plugin.v2.json。")).toBeVisible();
  await expect(page.getByRole("button", { name: "确认记在这台电脑上" })).toBeDisabled();
  await page.screenshot({ path: "e2e/screenshots/import-rejected-1100-light.png", fullPage: true });
  await theme(page, "深色");
  await page.setViewportSize({ width: 1100, height: 800 });
  await page.goto("/market/import");
  await page.getByLabel("选择文件").setInputFiles({ name: "nope.json", mimeType: "application/json", buffer: Buffer.from("{}") });
  await expect(page.getByText("这不是插件清单。请选择 plugin.v2.json。")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/import-rejected-1100-dark.png", fullPage: true });
  await page.goto("/market/installed");
  await expect(page.getByText("合成备忘")).toBeVisible();
  await expect(page.getByText("已安装，尚未启用").first()).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/import-installed-1100-dark.png", fullPage: true });
});
