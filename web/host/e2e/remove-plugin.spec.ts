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

async function enable(page: Page, name: string) {
  await page.goto("/market/installed");
  await page.locator(".catalog").getByRole("button", { name: new RegExp(name) }).click();
  await page.locator(".detail-card").getByRole("button", { name: "启用" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "启用" }).click();
  await expect(page.locator(".detail-card").getByRole("button", { name: "打开" })).toBeVisible();
}

test("a recorded plugin can be removed after confirmation and imported again", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await theme(page, "浅色");
  await importFile(page, "notes.plugin.v2.json", manifest("com.example.synthetic-notes", "合成备忘"));
  await enable(page, "合成备忘");
  await page.getByRole("button", { name: "把 合成备忘 固定到左侧" }).click();
  await expect(page.locator(".detail-card").getByRole("button", { name: "卸掉" })).toBeVisible();
  await expect(page.locator(".detail-card").getByText("不会删除你当初选中的原文件")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/remove-ready-1100-light.png", fullPage: true });
  await page.locator(".detail-card").getByRole("button", { name: "卸掉" }).click();
  await expect(page.getByRole("dialog", { name: "卸掉「合成备忘」" })).toBeVisible();
  await expect(page.getByText("也不会删除你当初选中的原文件。")).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/remove-confirm-1100-light.png", fullPage: true });
  await page.getByRole("dialog").getByRole("button", { name: "卸掉" }).click();
  await expect(page.getByText("合成备忘")).toHaveCount(0);
  await page.screenshot({ path: "e2e/screenshots/remove-gone-1100-light.png", fullPage: true });
  await page.goto("/plugins/local/com.example.synthetic-notes");
  await expect(page.getByRole("heading", { name: "这台电脑上没有这个记下的插件" })).toBeVisible();
  await importFile(page, "notes.plugin.v2.json", manifest("com.example.synthetic-notes", "合成备忘"));
  await page.goto("/market/installed");
  await expect(page.getByText("已安装，尚未启用")).toBeVisible();

  await theme(page, "深色");
  await page.setViewportSize({ width: 1100, height: 800 });
  await enable(page, "合成备忘");
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page.screenshot({ path: "e2e/screenshots/remove-ready-1100-dark.png", fullPage: true });
  await page.locator(".detail-card").getByRole("button", { name: "卸掉" }).click();
  await page.screenshot({ path: "e2e/screenshots/remove-confirm-1100-dark.png", fullPage: true });
  await page.getByRole("dialog").getByRole("button", { name: "卸掉" }).click();
  await expect(page.getByText("合成备忘")).toHaveCount(0);
  await page.screenshot({ path: "e2e/screenshots/remove-gone-1100-dark.png", fullPage: true });
});

test("remove explains a failure and leaves the recorded plugin in place", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await theme(page, "浅色");
  await importFile(page, "stuck.plugin.v2.json", manifest("com.example.synthetic-stuck", "卸不掉的备忘"));
  await page.goto("/market/installed");
  await page.locator(".catalog").getByRole("button", { name: /卸不掉的备忘/ }).click();
  await page.locator(".detail-card").getByRole("button", { name: "卸掉" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "卸掉" }).click();
  await expect(page.getByText("没有卸掉。记下的清单还在。")).toBeVisible();
  await expect(page.getByText("卸不掉的备忘").first()).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/remove-failed-1100-light.png", fullPage: true });
  await theme(page, "深色");
  await page.setViewportSize({ width: 1100, height: 800 });
  await page.goto("/market/installed");
  await page.locator(".catalog").getByRole("button", { name: /卸不掉的备忘/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page.locator(".detail-card").getByRole("button", { name: "卸掉" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "卸掉" }).click();
  await expect(page.getByText("没有卸掉。记下的清单还在。")).toBeVisible();
  await expect(page.getByText("卸不掉的备忘").first()).toBeVisible();
  await page.screenshot({ path: "e2e/screenshots/remove-failed-1100-dark.png", fullPage: true });
});
