// @ts-check
/* Browser tests for the tool's HTML UI (src/Oliver4.IconLibrary/Web).
 * The page is served from disk through a fake origin and talks to test-host.js instead of the C# host. */
const { test, expect } = require("@playwright/test");
const fs = require("fs");
const path = require("path");
const zlib = require("zlib");

const WEB = path.resolve(__dirname, "../../src/Oliver4.IconLibrary/Web");
const ORIGIN = "http://iconlibrary.test";
const CATALOGUE = zlib.gunzipSync(fs.readFileSync(path.join(WEB, "catalogue.json.gz")));
const TYPES = { ".html": "text/html", ".js": "text/javascript", ".css": "text/css", ".png": "image/png", ".json": "application/json" };

async function open(page, config = {}) {
  const errors = [];
  page.on("pageerror", e => errors.push(e.message));
  // anything the Content-Security-Policy blocks in normal use would be a regression
  page.on("console", m => { if (m.type() === "error" && /Content Security Policy/i.test(m.text())) errors.push(m.text()); });
  await page.route(ORIGIN + "/**", route => {
    const rel = decodeURIComponent(new URL(route.request().url()).pathname).replace(/^\//, "");
    if (rel === "catalogue.json") return route.fulfill({ body: CATALOGUE, contentType: "application/json" });
    if (rel === "mock.js") return route.fulfill({ status: 404, body: "" });
    const file = path.join(WEB, rel);
    if (!file.startsWith(WEB) || !fs.existsSync(file)) return route.fulfill({ status: 404, body: "" });
    return route.fulfill({ body: fs.readFileSync(file), contentType: TYPES[path.extname(file)] || "application/octet-stream" });
  });
  await page.addInitScript(cfg => { window.__TEST_CONFIG = cfg; }, config);
  await page.addInitScript({ path: path.join(__dirname, "test-host.js") });
  await page.goto(ORIGIN + "/index.html");
  return errors;
}

const calls = (page, method) => page.evaluate(m => window.__calls.filter(c => c.method === m).map(c => c.params), method);
const rows = page => page.locator("#body .list-row");

async function toTables(page, solution = "Core env1") {
  await rows(page).filter({ hasText: solution }).click();
  await page.getByRole("button", { name: "Next: select a table" }).click();
  await expect(rows(page).first()).toBeVisible();
}

async function toBrowse(page, table = "Batch", solution = "Core env1") {
  await toTables(page, solution);
  await rows(page).filter({ hasText: table }).first().click();
  await page.getByRole("button", { name: "Next: choose an icon" }).click();
  await expect(page.locator(".tile").first()).toBeVisible();
}

async function pickIcon(page, search, name) {
  await page.locator(".cats-search input").fill(search);
  await page.locator(".tile", { has: page.locator(".n", { hasText: new RegExp("^" + name + "$") }) }).click();
}

async function openConfirm(page) {
  await page.getByRole("button", { name: "Review and apply" }).click();
  await expect(page.locator(".modal .cstep").first()).toBeVisible();
}

// ------------------------------------------------------------------ start-up and navigation

test("UI-01 not connected shows the connect message", async ({ page }) => {
  const errors = await open(page, { connected: false });
  await expect(page.locator(".empty-title")).toHaveText("Connect to an environment");
  await expect(page.locator("#connText")).toHaveText("Not connected");
  await expect(page.locator("#btnReload")).toBeDisabled();
  expect(errors).toEqual([]);
});

test("UI-02 solutions load on connect, Default last, and search filters by name or unique name", async ({ page }) => {
  const errors = await open(page);
  await expect(rows(page)).toHaveCount(3);
  await expect(rows(page).last()).toContainText("Default Solution");
  await expect(page.getByRole("button", { name: "Next: select a table" })).toBeDisabled();
  await page.locator("#body .search input").fill("contoso_extra");
  await expect(rows(page)).toHaveCount(1);
  await expect(rows(page)).toContainText("Extra env1");
  await expect(page.locator("#connText")).toHaveText("env1.crm11.dynamics.com");
  expect((await calls(page, "warmCaches")).length).toBe(1);
  expect(errors).toEqual([]);
});

test("UI-03 tables show their icon state and search by logical name", async ({ page }) => {
  await open(page);
  await toTables(page);
  await expect(rows(page)).toHaveCount(4);
  await expect(rows(page).filter({ hasText: "Batch" }).locator(".state-text")).toHaveText("No icon set");
  await expect(rows(page).filter({ hasText: "Context" }).locator(".state-text")).toHaveText("Custom icon");
  await expect(rows(page).filter({ hasText: "Result" }).locator(".state-text")).toHaveText("Broken icon reference");
  await page.locator("#body .search input").fill("contoso_res");
  await expect(rows(page)).toHaveCount(1);
});

test("UI-04 icon search and category filter work together", async ({ page }) => {
  await open(page);
  await toBrowse(page);
  const total = JSON.parse(CATALOGUE.toString()).icons.length;
  await expect(page.locator("#gridMeta")).toContainText(`of ${total.toLocaleString("en-GB")} icons`);
  await page.locator(".cat", { hasText: "People" }).click();
  await page.locator(".cats-search input").fill("add");
  await expect(page.locator("#gridMeta")).toContainText('matching "add"');
  const names = await page.locator(".tile .n").allTextContents();
  expect(names.length).toBeGreaterThan(0);
  const cat = JSON.parse(CATALOGUE.toString());
  const expected = cat.icons.filter(i => i.categories.includes("people") && (i.name + " " + i.id.replace(/_/g, " ") + " " + i.keywords.join(" ")).toLowerCase().includes("add"));
  await expect(page.locator("#gridMeta")).toContainText(`${expected.length.toLocaleString("en-GB")} of`);
  expect(names[0].toLowerCase().startsWith("add") || names[0].toLowerCase().includes("add")).toBeTruthy();
});

test("UI-05 selecting an icon updates the stepper and preview; nothing is saved yet", async ({ page }) => {
  await open(page);
  await toBrowse(page);
  await expect(page.getByRole("button", { name: "Review and apply" })).toBeDisabled();
  await pickIcon(page, "stack", "Stack");
  await expect(page.locator(".step").nth(2).locator(".step-value")).toHaveText("Stack");
  await expect(page.locator(".icon-preview .t1")).toHaveText("Stack");
  await expect(page.locator(".sitemap .itm.you .lbl")).toHaveText("Batch");
  await expect(page.getByRole("button", { name: "Review and apply" })).toBeEnabled();
  expect(await calls(page, "apply")).toEqual([]);
});

// ------------------------------------------------------------------ confirmation panel

test("UI-06 create is the default for a table with no icon; the name is checked before saving", async ({ page }) => {
  await open(page);
  await toBrowse(page);
  await pickIcon(page, "stack", "Stack");
  await openConfirm(page);
  await expect(page.locator(".radio-row.on")).toContainText("Create new web resource");
  await expect(page.locator(".name-row .pfx")).toHaveText("contoso_");
  const name = page.locator("#logicalName");
  await expect(name).toHaveValue("stack_regular.svg");
  await expect(page.locator("#nameCheck")).toHaveText("✓ name available");
  await expect(page.locator("#btnSavePublish")).toBeEnabled();

  await name.fill("stack regular.svg");
  await expect(page.locator("#nameCheck")).toHaveText("✕ no spaces allowed");
  await expect(page.locator("#btnSavePublish")).toBeDisabled();
  await name.fill("icons//stack.svg");
  await expect(page.locator("#nameCheck")).toHaveText("✕ no consecutive slashes");
  await name.fill("stack.png");
  await expect(page.locator("#nameCheck")).toHaveText("✕ should end in .svg");
  await name.fill("taken.svg");
  await expect(page.locator("#nameCheck")).toHaveText("✕ name already in use");
  await expect(page.locator("#btnSave")).toBeDisabled();
});

test("UI-07 update is the default for an unmanaged custom icon and warns about shared tables", async ({ page }) => {
  await open(page);
  await toBrowse(page, "Context");
  await pickIcon(page, "stack", "Stack");
  await openConfirm(page);
  await expect(page.locator(".radio-row.on")).toContainText("Update existing web resource");
  await expect(page.locator(".notice.warn").first()).toContainText("1 other table uses");
  await expect(page.locator(".tbl-list")).toContainText("other_shared");
  await expect(page.locator("#btnSavePublish")).toBeEnabled();
});

test("UI-08 update is not offered when the current icon is managed", async ({ page }) => {
  await open(page);
  await toBrowse(page, "Partner");
  await pickIcon(page, "stack", "Stack");
  await openConfirm(page);
  const update = page.locator(".radio-row", { hasText: "Update existing web resource" });
  await expect(update).toBeDisabled();
  await expect(update).toContainText("current icon is managed");
  await expect(page.locator(".radio-row.on")).toContainText("Create new web resource");
});

test("UI-09 changing the publisher changes the prefix and warns about the mismatch", async ({ page }) => {
  await open(page);
  await toBrowse(page);
  await pickIcon(page, "stack", "Stack");
  await openConfirm(page);
  await page.locator(".modal select").selectOption("env1-p2");
  await expect(page.locator(".name-row .pfx")).toHaveText("oth_");
  await expect(page.locator(".notice.warn")).toContainText("Prefix won't match the solution's publisher");
  await expect(page.locator("#nameCheck")).toHaveText("✓ name available");
  await page.locator("#btnSavePublish").click();
  await expect(page.locator(".result-hero")).toBeVisible();
  expect((await calls(page, "apply"))[0].webResourceName).toBe("oth_stack_regular.svg");
});

test("UI-10 the Default Solution shows its notice", async ({ page }) => {
  await open(page);
  await toBrowse(page, "Batch", "Default Solution");
  await pickIcon(page, "stack", "Stack");
  await openConfirm(page);
  await expect(page.locator(".notice.info")).toContainText("Default Solution selected");
  await expect(page.locator(".name-row .pfx")).toHaveText("cr7a2_");
});

test("UI-11 reuse is offered when the icon already exists and sends its id", async ({ page }) => {
  await open(page);
  await toBrowse(page);
  await pickIcon(page, "text bullet list", "Text Bullet List");
  await openConfirm(page);
  await page.locator(".radio-row", { hasText: "Reuse an existing web resource" }).click();
  await expect(page.locator("#btnSavePublish")).toBeDisabled();
  await page.locator(".radio-row", { hasText: "contoso_/icons/text_bullet_list_regular.svg" }).click();
  await page.locator("#btnSavePublish").click();
  await expect(page.locator(".result-hero")).toBeVisible();
  const req = (await calls(page, "apply"))[0];
  expect(req).toMatchObject({ action: "reuse", webResourceId: "w1", webResourceName: "contoso_/icons/text_bullet_list_regular.svg", publish: true });
});

// ------------------------------------------------------------------ apply

test("UI-12 Save and Publish sends the full request, shows the result and refreshes the table", async ({ page }) => {
  const errors = await open(page);
  await toBrowse(page);
  await pickIcon(page, "stack", "Stack");
  await openConfirm(page);
  await page.locator(".modal-body input").nth(1).fill("  My Stack  ");
  await page.locator("#btnSavePublish").click();
  await expect(page.locator(".result-hero .t1")).toHaveText("Icon applied and published");
  const req = (await calls(page, "apply"))[0];
  expect(req).toEqual({
    tableLogicalName: "contoso_batch", tableDisplayName: "Batch", iconId: "stack", action: "create", publish: true,
    solutionId: "env1-s1", solutionUniqueName: "contoso_core", isDefaultSolution: false,
    webResourceName: "contoso_stack_regular.svg", webResourceDisplayName: "My Stack", webResourceId: null
  });
  await expect(page.locator(".prow.done")).toHaveCount(4);
  expect(errors).toEqual([]);
  await page.getByRole("button", { name: "Next table" }).click();
  await expect(rows(page).filter({ hasText: "Batch" }).locator(".state-text")).toHaveText("Custom icon");
  await expect(page.locator(".step").nth(2).locator(".step-value")).toHaveText("—");
});

test("UI-13 Save without publishing says so", async ({ page }) => {
  await open(page);
  await toBrowse(page);
  await pickIcon(page, "stack", "Stack");
  await openConfirm(page);
  await page.locator("#btnSave").click();
  await expect(page.locator(".result-hero .t1")).toHaveText("Icon saved (not published)");
  expect((await calls(page, "apply"))[0].publish).toBe(false);
  await expect(page.locator(".prow.skipped")).toContainText("Publish");
});

test("UI-14 a failure says exactly what completed and what was not attempted", async ({ page }) => {
  await open(page, { failStep: "table" });
  await toBrowse(page);
  await pickIcon(page, "stack", "Stack");
  await openConfirm(page);
  await page.locator("#btnSavePublish").click();
  await expect(page.locator(".result-hero .t1")).toHaveText("Update table icon failed");
  await expect(page.locator(".result-text")).toContainText("Completed: create web resource, add to solution.");
  await expect(page.locator(".result-text")).toContainText("Not attempted: publish web resource and table.");
  await expect(page.locator(".err-detail")).toHaveText("missing prvWriteEntity");
  await expect(page.getByRole("button", { name: "Retry" })).toBeVisible();
});

test("UI-15 the progress panel cannot be closed while applying", async ({ page }) => {
  await open(page, { holdApply: true });
  await toBrowse(page);
  await pickIcon(page, "stack", "Stack");
  await openConfirm(page);
  await page.locator("#btnSavePublish").click();
  await expect(page.locator(".modal-hd .t")).toHaveText("Applying icon…");
  await page.keyboard.press("Escape");
  await page.locator(".overlay").click({ position: { x: 5, y: 5 } });
  await expect(page.locator(".modal-hd .t")).toHaveText("Applying icon…");
  for (let i = 0; i < 4; i++) await page.evaluate(() => window.__release("apply"));
  await expect(page.locator(".modal-hd .t")).toHaveText("Done");
});

test("UI-16 About shows the version and the licence notices", async ({ page }) => {
  await open(page);
  await page.locator("#btnAbout").click();
  await expect(page.locator(".about .ver")).toHaveText("Version 9.9.9");
  await expect(page.locator(".licence")).toContainText("Microsoft Corporation");
  await page.keyboard.press("Escape");
  await expect(page.locator(".modal")).toHaveCount(0);
});

// ------------------------------------------------------------------ regressions

test("UI-17 after switching environment the publishers come from the new environment", async ({ page }) => {
  await open(page);
  await toBrowse(page);
  await pickIcon(page, "stack", "Stack");
  await openConfirm(page);
  await page.getByRole("button", { name: "Cancel" }).click();

  // XrmToolBox connection changed to another environment
  await page.evaluate(() => { window.__testHost.setEnv("env2"); window.__host.emit("context", { context: { connected: true, orgUrl: "env2.crm11.dynamics.com" }, reload: true }); });
  await expect(rows(page).first()).toContainText("Core env2");
  await toBrowse(page, "Batch", "Core env2");
  await pickIcon(page, "stack", "Stack");
  await openConfirm(page);
  await expect(page.locator(".modal select option")).toHaveText(["Fabrikam  (prefix fab_)", "Other env2  (prefix oth_)", "Default Publisher  (prefix cr7a2_)"]);
  await expect(page.locator(".name-row .pfx")).toHaveText("fab_");
});

test("UI-18 changing solution while its tables are still loading loads the new solution's tables", async ({ page }) => {
  await open(page, { holdTablesFor: "env1-s1" });
  await rows(page).filter({ hasText: "Core env1" }).click();
  await page.getByRole("button", { name: "Next: select a table" }).click();
  await expect(page.locator(".list-meta")).toContainText("Loading tables in Core env1");
  // change your mind before the slow list arrives
  await page.locator(".step-change").first().click();
  await rows(page).filter({ hasText: "Extra env1" }).click();
  await page.getByRole("button", { name: "Next: select a table" }).click();
  await page.evaluate(() => window.__release("tables:env1-s1"));
  await expect(rows(page)).toHaveCount(1, { timeout: 3000 });
  await expect(rows(page)).toContainText("Extra One");
});

test("UI-19 an SVG read from the environment cannot run script in the tool", async ({ page }) => {
  const errors = await open(page, { maliciousSvg: '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16"><image href="nope.png" onerror="window.__pwned=1"/><foreignObject><img src="x" onerror="window.__pwned=2"/></foreignObject><path d="M1 1h14v14H1z" onmouseover="window.__pwned=3"/></svg>' });
  await toTables(page);
  await rows(page).filter({ hasText: "Context" }).click();
  await page.getByRole("button", { name: "Next: choose an icon" }).click();
  await page.locator(".rail .glyph.custom").hover();
  await page.waitForTimeout(500);
  expect(await page.evaluate(() => window.__pwned)).toBeUndefined();
  await expect(rows(page)).toHaveCount(0); // on the browse screen now
  await expect(page.locator(".rail .glyph.custom svg path")).toHaveCount(1); // the drawing itself is still shown
  expect(errors).toEqual([]);
});

// ------------------------------------------------------------------ background reads, naming and reuse

test("UI-20 existing web resource content is read in the background once the tables load", async ({ page }) => {
  await open(page);
  await toTables(page);
  await expect.poll(async () => (await calls(page, "warmSvgContent")).length).toBe(2); // 2 left, then 1, then 0 stops
  await page.waitForTimeout(100);
  expect((await calls(page, "warmSvgContent")).length).toBe(2);
});

test("UI-21 Logical Name and Display Name are labelled, required, and the display name defaults to the icon name", async ({ page }) => {
  const errors = await open(page);
  await toBrowse(page);
  await pickIcon(page, "stack", "Stack");
  await openConfirm(page);
  await expect(page.locator("label.flabel")).toHaveText(["Logical Name *", "Display Name *"]);
  await expect(page.locator("#dispName")).toHaveValue("Stack");
  await expect(page.locator("#btnSavePublish")).toBeEnabled();
  await page.locator("#dispName").fill("   ");
  await expect(page.locator("#dispCheck")).toContainText("display name is required");
  await expect(page.locator("#btnSavePublish")).toBeDisabled();
  await expect(page.locator("#btnSave")).toBeDisabled();
  await page.locator("#dispName").fill("Stack icon");
  await expect(page.locator("#btnSavePublish")).toBeEnabled();
  await page.locator("#logicalName").fill("");
  await expect(page.locator("#btnSavePublish")).toBeDisabled();
  expect(errors).toEqual([]);
});

test("UI-22 the existing web resources to reuse are a separate step from the action", async ({ page }) => {
  await open(page);
  await toBrowse(page);
  await pickIcon(page, "text bullet list", "Text Bullet List");
  await openConfirm(page);
  await page.locator(".radio-row", { hasText: "Reuse an existing web resource" }).click();
  const steps = page.locator(".modal .cstep");
  await expect(steps).toHaveCount(3);
  await expect(steps.nth(0).locator(".cstep-t")).toHaveText("Choose the action");
  await expect(steps.nth(0).locator(".radio-row", { hasText: "contoso_/icons/text_bullet_list_regular.svg" })).toHaveCount(0);
  await expect(steps.nth(1).locator(".cstep-t")).toHaveText("Choose the existing web resource");
  await expect(steps.nth(1).locator(".radio-row", { hasText: "contoso_/icons/text_bullet_list_regular.svg" })).toHaveCount(1);
  await expect(steps.nth(1).locator(".cstep-n")).toHaveText("2");
  await expect(steps.nth(2).locator(".cstep-n")).toHaveText("3");
});
