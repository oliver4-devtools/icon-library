// @ts-check
const { defineConfig } = require("@playwright/test");

module.exports = defineConfig({
  testDir: ".",
  testMatch: /.*\.spec\.js/,
  timeout: 30000,
  expect: { timeout: 5000 },
  fullyParallel: true,
  reporter: [["list"]],
  use: {
    browserName: "chromium",
    // WebView2 is Chromium, so this is the closest browser to what XrmToolBox uses
    viewport: { width: 1240, height: 740 },
    locale: "en-GB"
  }
});
