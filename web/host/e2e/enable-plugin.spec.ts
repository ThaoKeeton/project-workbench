import { expect, test, type Page } from "@playwright/test";

function manifest(id: string, name: string) {
  return JSON.stringify({
    contract: "workbench.plugin.v2",
    contractVersion: "2.0.0-preview.1",
    id,
    name,
    version: "0.1.0",
    description: "只留在本机的合成清单。",
    autoSend: false,
    backend: { entryAssembly: "Notes.dll", entryType: "Example.Notes" },
    ui: { kind: "web-package", module: "./ui/Notes.tsx", mount: "embedded" },
    permissions: [{ id: "project.read", reason: "知道当前项目", sensitive: false }],
  });
}

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

async function importFile(page: Page, name: string, body: string) {
  await page.goto("/market/import");
  await page.getByLabel("选择文件").setInputFiles({ name, mimeType: "application/json", buffer: Buffer.from(body) });
  await page.getByRole("button", { name: "确认记在这台电脑上" }).click();
  await expect(page.getByText("已记在这台电脑上，还没有启用。")).toBeVisible();
}

test("a recorded plugin stays off until enable, then opens and can be pinned", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await theme(page, "浅色");
  await importFile(page, "notes.plugin.v2.json", manifest("com.example.synthetic-notes", "合成备忘"));
  await page.goto("/market/installed");
  await expect(page.getByText("已安装，尚未启用")).toBeVisible();
  await expect(page.getByRole("button", { name: "启用" }).first()).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/enable-waiting-1100-light.png", fullPage: true });
  await page.locator(".catalog").getByRole("button", { name: /合成备忘/ }).click();
  await page.locator(".detail-card").getByRole("button", { name: "启用" }).click();
  await expect(page.getByRole("dialog", { name: "启用「合成备忘」" })).toBeVisible();
  await expect(page.getByText("只会加载这台电脑上已经记下的清单。不会联网，也不会下载。")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/enable-confirm-1100-light.png", fullPage: true });
  await page.getByRole("dialog").getByRole("button", { name: "启用" }).click();
  await expect(page.locator(".detail-card").getByRole("button", { name: "打开" })).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/enable-ready-1100-light.png", fullPage: true });
  await page.getByRole("button", { name: "把 合成备忘 固定到左侧" }).click();
  await expect(page.getByText("已固定在左侧").first()).toBeVisible();
  await page.locator(".detail-card").getByRole("button", { name: "打开" }).click();
  await expect(page.getByText("已在这台电脑上启用。打开的是记下的清单。没有附带后端程序，也没有从网上下载。")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/enable-open-1100-light.png", fullPage: true });
  await page.reload();
  await expect(page.getByRole("heading", { name: "合成备忘" })).toBeVisible();

  await theme(page, "深色");
  await page.setViewportSize({ width: 1100, height: 800 });
  await page.goto("/market/installed");
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page.locator(".catalog").getByRole("button", { name: /合成备忘/ }).click();
  await expect(page.locator(".detail-card").getByRole("button", { name: "打开" })).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/enable-ready-1100-dark.png", fullPage: true });
  await page.goto("/plugins/local/com.example.synthetic-notes");
  await expect(page.getByRole("heading", { name: "合成备忘" })).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/enable-open-1100-dark.png", fullPage: true });
});

test("enable refuses a recorded manifest that is no longer readable", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await theme(page, "浅色");
  await importFile(page, "gap.plugin.v2.json", manifest("com.example.synthetic-gap", "打不开的备忘"));
  await page.goto("/market/installed");
  await page.locator(".catalog").getByRole("button", { name: /打不开的备忘/ }).click();
  await page.locator(".detail-card").getByRole("button", { name: "启用" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "启用" }).click();
  await expect(page.getByText("没有启用。记下的清单已经不在。")).toBeVisible();
  await expect(page.getByText("已安装，尚未启用").first()).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/enable-failed-1100-light.png", fullPage: true });
  await theme(page, "深色");
  await page.setViewportSize({ width: 1100, height: 800 });
  await page.goto("/market/installed");
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page.locator(".catalog").getByRole("button", { name: /打不开的备忘/ }).click();
  await page.screenshot({ path: "e2e/screenshots/enable-waiting-1100-dark.png", fullPage: true });
  await page.locator(".detail-card").getByRole("button", { name: "启用" }).click();
  await expect(page.getByRole("dialog", { name: "启用「打不开的备忘」" })).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/enable-confirm-1100-dark.png", fullPage: true });
  await page.getByRole("dialog").getByRole("button", { name: "启用" }).click();
  await expect(page.getByText("没有启用。记下的清单已经不在。")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/enable-failed-1100-dark.png", fullPage: true });
});
