import test from "node:test";
import assert from "node:assert/strict";
import { chromium } from "playwright-core";
import { readFile } from "node:fs/promises";
const browserPath = process.env.COFRE_TEST_BROWSER;
const popup = await readFile(new URL("../popup.html", import.meta.url), "utf8");
const script = await readFile(new URL("../popup.js", import.meta.url), "utf8");
const css = await readFile(new URL("../popup.css", import.meta.url), "utf8");
async function setup(t, permission) {
  const browser = await chromium.launch({ executablePath: browserPath, headless: true });
  t.after(() => browser.close());
  const page = await browser.newPage();
  await page.route("https://popup.test/**", route => {
    const path = new URL(route.request().url()).pathname;
    route.fulfill({ body: path === "/popup.js" ? script : path === "/popup.css" ? css : path === "/" ? popup : "",
      contentType: path.endsWith(".js") ? "text/javascript" : path.endsWith(".css") ? "text/css" : "text/html" });
  });
  await page.addInitScript(granted => {
    globalThis.calls = []; globalThis.siteGranted = granted;
    globalThis.chrome = {
      tabs: { query: async () => [{ id: 1, url: "https://example.com/login" }] },
      permissions: { contains: async () => globalThis.siteGranted,
        request: async request => { calls.push({ permission: request }); globalThis.siteGranted = true; return true; } },
      runtime: { sendMessage: message => {
        calls.push(message);
        if (message.action === "list") return new Promise(resolve => { globalThis.finishList = resolve; });
        if (message.action === "attach") return Promise.resolve({ ok: true, fieldCount: 1 });
        if (message.action === "fill") return new Promise(resolve => { globalThis.finishFill = resolve; });
        return Promise.resolve({ ok: true });
      } }
    };
  }, permission);
  await page.goto("https://popup.test/");
  await page.waitForFunction(() => !!globalThis.finishList);
  return page;
}
test("popup keeps site activation available while the vault request is pending or locked", { skip: !browserPath }, async t => {
  const page = await setup(t, false);
  await page.waitForFunction(() => document.querySelector("#field-status").textContent.includes("não estão ativados"));
  assert.equal(await page.locator("#attach").isEnabled(), true);
  assert.equal(await page.locator("#refresh").isEnabled(), false);
  await page.locator("#attach").click();
  await page.waitForFunction(() => document.querySelector("#field-status").textContent.includes("Ícones ativos em 1"));
  assert.deepEqual(await page.evaluate(() => calls.find(x => x.permission).permission.origins), ["https://example.com/*"]);
  await page.evaluate(() => finishList({ ok: false, error: "Desbloqueie o Cofre." }));
  await page.waitForFunction(() => document.querySelector("#status").textContent === "Desbloqueie o Cofre.");
  assert.match(await page.locator("#field-status").textContent(), /Ícones ativos/);
  assert.equal(await page.locator("#attach").isEnabled(), true);
});
test("popup automatically restores icons and leaves activation available while filling or awaiting unlock", { skip: !browserPath }, async t => {
  const page = await setup(t, true);
  await page.waitForFunction(() => document.querySelector("#field-status").textContent.includes("Ícones ativos"));
  await page.evaluate(() => finishList({ ok: true, pageUrl: "https://example.com/login", entries: [{id:"id",title:"Demo",username:"test-only"}] }));
  await page.locator("#entries button").click();
  await page.waitForFunction(() => !!globalThis.finishFill);
  assert.match(await page.locator("#status").textContent(), /Preenchendo.*desbloqueie/);
  assert.equal(await page.locator("#attach").isEnabled(), true);
  await page.locator("#attach").click();
  await page.waitForFunction(() => calls.filter(x => x.action === "attach").length === 2);
  await page.evaluate(() => finishFill({ ok: true }));
  await page.waitForFunction(() => document.querySelector("#status").textContent.includes("Login preenchido"));
});
