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
  const rail = page.getByRole("navigation", { name: "主导航" });
  await expect(rail.getByRole("button", { name: "UPCAM 站会日报" })).toBeVisible();
  await expect(rail.getByRole("button", { name: "钉钉 Insights" })).toBeVisible();
  await expect(rail.getByRole("button", { name: "测试设计" })).toBeVisible();
      await expect(rail.getByRole("button", { name: "自动测试" })).toBeVisible();
  await page.keyboard.press("Control+k");
  await page.getByRole("option", { name: "灯塔实验" }).click();
  await expect(page.locator(".context-project")).toContainText("灯塔实验");
});

test("sample cases stay on their own local page", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await chooseTheme(page, "深色");
  await page.goto("/plugins/test-cases");
  const step = page.getByRole("textbox", { name: "EX-001 步骤" });
  await expect(step).toHaveValue("启用本地插件");
  await step.fill("查看本地示例");
  await page.getByRole("button", { name: "保存示例草稿" }).click();
  await expect(page.getByRole("status")).toHaveText("已保存到本机，未发送。");
  await page.reload();
  await expect(page.getByRole("textbox", { name: "EX-001 步骤" })).toHaveValue("查看本地示例");
});

test("launcher opens the synthetic test dashboard", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/");
  await page.getByRole("navigation", { name: "主导航" }).getByRole("button", { name: "测试设计" }).click();
  await expect(page.getByRole("heading", { name: "测试设计" })).toBeVisible();
  await expect(page.getByRole("note")).toContainText("不替代人工验收");
  await expect(page.getByRole("cell", { name: "SYN-TC-001", exact: true }).first()).toBeVisible();
  await expect(page.getByRole("cell", { name: "未验收" }).first()).toBeVisible();
});

test("a local standup file becomes today's draft and ding send stays closed", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/plugin/upcam");
  const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Shanghai", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  const notes = `日期: ${today}\n\n事项: SYN-044\n标题: 本机导入\n负责人: 界面\n状态: 进行中\n发言: 这条来自本机文件。\n`;
  await page.getByLabel("选择站会文件", { exact: true }).setInputFiles({ name: "notes.txt", mimeType: "text/plain", buffer: Buffer.from(notes) });
  await expect(page.getByRole("cell", { name: "SYN-044" })).toBeVisible();
  await expect(page.getByRole("cell", { name: "界面" })).toBeVisible();
  await expect(page.getByRole("status")).toHaveText("已从本机导入，未发送。");
  await page.reload();
  await expect(page.getByRole("cell", { name: "SYN-044" })).toBeVisible();

  const sends: string[] = [];
  page.on("request", (request) => {
    if (request.url().includes("/actions/send")) sends.push(request.url());
  });
  await page.goto("/plugin/ding");
  await page.getByRole("button", { name: "载入合成样本" }).click();
  await page.getByRole("button", { name: "按需分析" }).click();
  await expect(page.getByText("登录页对照出现 3 次")).toBeVisible();
  await expect(page.getByRole("button", { name: "发送已关闭" })).toBeDisabled();
  await page.getByRole("button", { name: "发送已关闭" }).click({ force: true });
  expect(sends).toEqual([]);
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

    test("wizard drafts, demos, and keeps a saved test on the home", async ({ page }) => {
      await page.addInitScript(() => {
        sessionStorage.setItem("workbench.author.fake", "pass");
        sessionStorage.setItem("workbench.author.pace", "700");
      });
      await page.goto("/plugin/runner");
      await page.getByRole("button", { name: "选择程序" }).click();
      await page.getByRole("button", { name: "合成电子样品" }).click();
      await expect(page.getByRole("status")).toContainText("已连接");
      await page.getByRole("button", { name: "下一步" }).click();
      await page.getByLabel("要测什么").fill("切削工件后，工件变小，旁边的牙不变");
      await page.getByRole("button", { name: "下一步" }).click();
      await expect(page.getByRole("heading", { name: "它会做" })).toBeVisible();
      await expect(page.getByRole("heading", { name: "它会核对" })).toBeVisible();
      await expect(page.getByText("旁边的牙保持原样")).toHaveCount(1);
      await expect(page.getByText("今天还能生成 20 次。")).toBeVisible();
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow).toBeLessThanOrEqual(1);
      await shot(page, `wizard-checklist-${theme}`);
      await page.getByRole("button", { name: "演示一遍" }).click();
      await expect(page.getByText("完成").first()).toBeVisible();
      await expect(page.getByText("通过", { exact: true })).toHaveCount(0);
      await shot(page, `wizard-demo-${theme}`);
      await expect(page.getByText("通过", { exact: true }).first()).toBeVisible();
      await expect(page.getByText("工件体积从 8 变成 4，小了 50%", { exact: true })).toBeVisible();
      await expect(page.getByRole("img", { name: "之后" })).toBeVisible();
      await shot(page, `wizard-pass-${theme}`);
      await page.getByRole("button", { name: "保存并设置每天重跑" }).click();
      await page.getByLabel("每天重跑的时间").fill("07:30");
      await shot(page, `wizard-schedule-${theme}`);
      await page.getByRole("button", { name: "保存安排" }).click();
      await page.getByRole("button", { name: "回到首页" }).click();
      await expect(page.getByRole("heading", { name: "已保存的测试" })).toBeVisible();
      await expect(page.getByRole("button", { name: "新建一个测试" })).toBeVisible();
      await expect(page.getByText(/下次 (今天|明天) 07:30/)).toBeVisible();
      await shot(page, `wizard-home-${theme}`);
    });

    test("home shows dated saved tests and can hide the ones that passed", async ({ page }) => {
      await page.addInitScript(() => sessionStorage.setItem("workbench.author.samples", "1"));
      await page.goto("/plugin/runner");
      await expect(page.getByRole("heading", { name: "已保存的测试" })).toBeVisible();
      await expect(page.getByRole("button", { name: "新建一个测试" })).toBeVisible();
      await expect(page.getByText(/上次 今天/).first()).toBeVisible();
      await expect(page.getByText(/下次 今天/).first()).toBeVisible();
      await expect(page.getByText(/下次 明天/).first()).toBeVisible();
      await expect(page.getByText(/\d+月\d+日/).first()).toBeVisible();
      await expect(page.getByText("没通过").first()).toBeVisible();
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow).toBeLessThanOrEqual(1);
      await shot(page, `wizard-library-${theme}`);
      await page.getByRole("group", { name: "筛选" }).getByRole("button", { name: "没通过" }).click();
      await expect(page.locator(".row-open", { hasText: "切削后体积减小" })).toHaveCount(0);
      await expect(page.locator(".row-open", { hasText: "旁边的牙变了" })).toBeVisible();
    });

    test("app picker, a live connection, an already-open app, and AI settings", async ({ page }) => {
      await page.goto("/plugin/runner");
      await expect(page.getByRole("note", { name: "第一次使用" })).toBeVisible();
      await expect(page.getByText("先选一个程序。")).toBeVisible();
      await expect(page.getByText("用一句话说明要检查什么。")).toBeVisible();
      await expect(page.getByText("看它做一遍，对了再保存。")).toBeVisible();
      await page.getByRole("button", { name: "知道了" }).click();
      await expect(page.getByRole("note", { name: "第一次使用" })).toHaveCount(0);
      await page.getByRole("button", { name: "选择程序" }).click();
      await expect(page.getByRole("heading", { name: "最近用过的" })).toBeVisible();
      await expect(page.getByRole("heading", { name: "这台电脑上的程序" })).toBeVisible();
      await expect(page.getByRole("button", { name: "浏览…" })).toBeVisible();
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow).toBeLessThanOrEqual(1);
      await shot(page, `wizard-apps-${theme}`);
      await page.getByRole("button", { name: "合成电子样品" }).click();
      await expect(page.getByRole("status")).toHaveText("已连接：合成电子样品");
      await expect(page.getByText("认出了 3D 画面，里面有 2 个物体")).toBeVisible();
      await expect(page.getByRole("img", { name: "程序画面" })).toBeVisible();
      await shot(page, `wizard-connected-${theme}`);
      await page.getByRole("button", { name: "已经开着的样品" }).click();
      await expect(page.getByRole("status")).toHaveText("这个程序已经开着，需要由工作台重新打开它");
      await expect(page.getByRole("button", { name: "关掉并重新打开" })).toBeVisible();
      await shot(page, `wizard-busy-${theme}`);
      await page.getByRole("button", { name: "关掉并重新打开" }).click();
      await page.getByRole("dialog", { name: "确认重新打开" }).getByRole("button", { name: "关掉并重新打开" }).click();
      await expect(page.getByRole("status")).toHaveText("已连接：已经开着的样品");
      await page.getByRole("button", { name: "记事本" }).click();
      await expect(page.getByRole("status")).toHaveText("这个程序还不能自动测试。");
      await page.getByLabel("浏览程序").setInputFiles({ name: "连不上的样品.exe", mimeType: "application/octet-stream", buffer: Buffer.from("x") });
      await expect(page.getByRole("status")).toHaveText("连上用的时间太长了。");
      await page.getByRole("button", { name: "再试一次" }).click();
      await expect(page.getByRole("status")).toHaveText("已连接：连不上的样品");
      await page.getByRole("button", { name: "下一步" }).click();
      await page.getByLabel("要测什么").fill("切削工件后，工件变小，旁边的牙不变");
      await page.getByRole("button", { name: "下一步" }).click();
      await page.getByRole("link", { name: "去设置" }).click();
      await expect(page.getByRole("heading", { name: "AI 设置" })).toBeVisible();
      await expect(page.getByText("每天重跑不会再用 AI。").first()).toBeVisible();
      await expect(page.getByLabel("密钥")).toHaveAttribute("type", "password");
      await shot(page, `wizard-ai-${theme}`);
      await page.evaluate(() => sessionStorage.setItem("workbench.author.fake", "1"));
      await page.getByLabel("密钥").fill("sample-key-0001");
      await page.getByRole("button", { name: "测试一下" }).click();
      await expect(page.getByRole("status")).toHaveText("试过了。可以开始生成。");
      await expect(page.getByRole("status")).not.toContainText("sample-key");
      await page.getByRole("button", { name: "保存" }).click();
      await expect(page.getByRole("heading", { name: "看它准备做什么" })).toBeVisible();
    });

    test("wizard explains a failed check in plain words", async ({ page }) => {
      await page.addInitScript(() => sessionStorage.setItem("workbench.author.fake", "fail"));
      await page.goto("/plugin/runner");
      await page.getByRole("button", { name: "选择程序" }).click();
      await page.getByRole("button", { name: "合成电子样品" }).click();
      await page.getByRole("button", { name: "下一步" }).click();
      await page.getByLabel("要测什么").fill("切削工件后，工件变小，旁边的牙不变");
      await page.getByRole("button", { name: "下一步" }).click();
      await page.getByLabel("哪里不对").fill("文字不该停在已开始");
      await page.getByRole("button", { name: "重新生成" }).click();
      await page.getByRole("button", { name: "演示一遍" }).click();
      await expect(page.getByText("没通过", { exact: true }).first()).toBeVisible();
      await expect(page.getByText("期望看到「不会出现」")).toBeVisible();
      await expect(page.getByText("实际看到「已开始」")).toBeVisible();
      await expect(page.getByRole("button", { name: "重新演示" })).toBeVisible();
      await expect(page.getByRole("button", { name: "修改检查" })).toBeVisible();
      await shot(page, `wizard-fail-${theme}`);
    });

    test("plugin, diagnostics, settings, pairing, offline, and crash", async ({ page }) => {
      await page.goto("/plugin/upcam");
      await expect(page.getByRole("heading", { name: "站会日报" })).toBeVisible();
      await expect(page.getByText("自动发送已关闭")).toBeVisible();
      await page.getByRole("button", { name: "载入合成样本" }).click();
      await expect(page.getByRole("cell", { name: "SYN-014" })).toBeVisible();
      await expect(page.getByRole("cell", { name: "界面" })).toBeVisible();
      await expect(page.getByText("登录页今天完成对照，明日交给合成检查。")).toBeVisible();
      await expect(page.getByRole("status")).toHaveText("已载入合成样本，未发送。");
      await shot(page, `plugin-upcam-${theme}`);
      await shot(page, `plugin-${theme}`);
      await page.goto("/plugin/ding");
      await page.getByRole("button", { name: "载入合成样本" }).click();
      await page.getByRole("button", { name: "按需分析" }).click();
      await expect(page.getByText("登录页对照出现 3 次")).toBeVisible();
      await expect(page.getByText("09 时有 5 条消息")).toBeVisible();
      await expect(page.getByRole("button", { name: "发送已关闭" })).toBeDisabled();
      await shot(page, `plugin-ding-${theme}`);
      await page.goto("/plugin/test");
      await expect(page.getByRole("heading", { name: "测试设计" })).toBeVisible();
      await expect(page.getByRole("note")).toContainText("不替代人工验收");
      await expect(page.getByRole("cell", { name: "SYN-TC-006", exact: true }).first()).toBeVisible();
      await expect(page.getByRole("cell", { name: "未验收" }).first()).toBeVisible();
      await shot(page, `plugin-test-${theme}`);
      await page.goto("/diagnostics");
      await expect(page.getByText("仅本机回环")).toBeVisible();
      await expect(page.getByText("自动发送：关闭")).toBeVisible();
      await expect(page.getByText("1,100")).toBeVisible();
      await shot(page, `diagnostics-${theme}`);
      await page.goto("/settings");
      await expect(page.getByRole("radio", { name: new RegExp(label) })).toBeChecked();
      await expect(page.getByLabel("每天最多生成几次")).toBeVisible();
      await expect(page.getByText("这里不写金额")).toBeVisible();
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
