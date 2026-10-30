import test from "node:test";
import assert from "node:assert/strict";
import { readFile, mkdir } from "node:fs/promises";
import { chromium } from "playwright-core";
import { formOperation } from "../page.js";
import { generatePassword, defaults } from "../generator.js";

const enabled = !!process.env.COFRE_TEST_BROWSER;
const source = await readFile(new URL("../content.js", import.meta.url), "utf8");
const url = "https://cofre.test/login";
const fixture = (body, title = "Página de teste") => `<!doctype html><html lang="pt-BR"><meta charset="utf-8"><title>${title}</title><style>
body{background:#f4f3ed;color:#242a20;font:16px system-ui;margin:70px auto;width:420px}h1{font-size:32px}label{display:block;margin:22px 0}input{display:block;width:100%;height:46px;padding:8px 70px 8px 12px;border:1px solid #bbc3af;background:#fbfaf5;font:16px system-ui}button{padding:12px 22px;background:#4d613b;color:white;border:0}</style>${body}</html>`;

async function setup(t, body, { realShadow = false, csp = false } = {}) {
  const browser = await chromium.launch({ executablePath: process.env.COFRE_TEST_BROWSER, headless: true });
  t.after(() => browser.close());
  const page = await browser.newPage({ viewport: { width: 1100, height: 1000 } });
  await page.route("https://cofre.test/**", route => route.fulfill({ body: fixture(body), contentType: "text/html",
    headers: csp ? { "content-security-policy": "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'" } : {} }));
  await page.goto(url);
  await page.evaluate(({ code, realShadow }) => {
    if (!realShadow) {
      const attach = Element.prototype.attachShadow;
      Element.prototype.attachShadow = function(options) {
        const root = attach.call(this, options);
        if (this.localName === "cofre-assistant") window.__testRoot = root;
        return root;
      };
    }
    globalThis.__requests = []; globalThis.__saved = []; globalThis.__options = null;
    globalThis.__operation = (0, eval)("(" + code + ")");
    globalThis.chrome = { runtime: { id: "test-extension", sendMessage: async message => {
      globalThis.__requests.push(message);
      const args = { field: message.field };
      try {
        if (message.action === "inline.inspect") return { ok: true, ...__operation(message.pageUrl, "inspect", args), options: globalThis.__defaults };
        if (message.action === "inline.list") return { ok: true, entries: [{ id: "account1", title: "Conta de teste", username: "demo@cofre.test" }] };
        if (message.action === "inline.generate") {
          __operation(message.pageUrl, "inspect", args);
          __options = message.options;
          return { ok: true, password: await globalThis.__testGenerate(message.options) };
        }
        if (message.action === "inline.apply") return { ok: true, ...__operation(message.pageUrl, "generated", { ...args, password: message.password }) };
        if (message.action === "inline.fill") return { ok: true, ...__operation(message.pageUrl, "fill", { ...args, username: "demo@cofre.test", password: "test-only-secret" }) };
        if (message.action === "inline.save") { __saved.push({ id: message.id, kind: message.kind, ...__operation(message.pageUrl, "read", args) }); return { ok: true }; }
        return { ok: false, error: "Unknown" };
      } catch (error) { return { ok: false, error: error.message }; }
    } } };
  }, { code: formOperation.toString(), realShadow });
  await page.exposeFunction("__testGenerate", generatePassword);
  await page.evaluate(o => { globalThis.__defaults = o; }, defaults);
  await page.addScriptTag({ content: source });
  return page;
}
async function part(page, selector) { return (await page.evaluateHandle(selector => __testRoot.querySelector(selector), selector)).asElement(); }
async function ready(page) { await page.waitForFunction(() => !__testRoot.querySelector(".panel").hidden && !__testRoot.querySelector("#save").disabled); }
async function click(page, selector) { const el = await part(page, selector); await el.click(); await ready(page); }
async function state(page, selector, property = "textContent") { return page.evaluate(({ selector, property }) => __testRoot.querySelector(selector)[property], { selector, property }); }
async function openFor(page, selector) {
  const index = await page.evaluate(selector => [...document.querySelectorAll('input[type="password"]')].indexOf(document.querySelector(selector)), selector);
  await (await part(page, `.field:nth-of-type(${index + 1})`)).click();
  await ready(page);
}
const login = '<h1>Entre na sua conta</h1><form><label>E-mail<input type="email" autocomplete="username"></label><label>Senha<input id="password" type="password" autocomplete="current-password"></label><button>Entrar</button></form>';
const signup = '<h1>Crie sua conta</h1><form><label>E-mail<input type="email" value="novo@cofre.test"></label><label>Senha<input id="password" type="password" autocomplete="new-password" minlength="16" maxlength="32"></label><label>Confirme<input id="confirm" type="password"></label><button>Cadastrar</button></form>';

test("real DOM: focus suggests logins, explicit selection fills and never submits", { skip: !enabled }, async t => {
  const page = await setup(t, login);
  await page.evaluate(() => { document.querySelector("form").addEventListener("submit", e => { e.preventDefault(); globalThis.__submitted = true; }); });
  await page.locator("#password").click();
  assert.equal(await state(page, ".panel", "hidden"), true);
  await openFor(page, "#password");
  assert.match(await state(page, ".hint"), /Login identificado/);
  assert.match(await state(page, "#entries"), /demo@cofre.test/);
  assert.equal(await page.locator("#password").inputValue(), "");
  await click(page, "#entries button");
  assert.equal(await page.locator("input[type=email]").inputValue(), "demo@cofre.test");
  assert.equal(await page.locator("#password").inputValue(), "test-only-secret");
  assert.equal(await page.evaluate(() => !!globalThis.__submitted), false);
  assert.equal(await page.evaluate(() => document.querySelector("cofre-assistant").shadowRoot), null);
});
test("real DOM: signup generator fills confirmation, remembers options and saves selected account", { skip: !enabled }, async t => {
  const page = await setup(t, signup);
  await openFor(page, "#password");
  assert.match(await state(page, ".hint"), /Cadastro identificado/);
  assert.equal(await state(page, "#generator", "hidden"), false);
  const initial = await state(page, ".preview", "value"); assert.equal(initial.length, 20);
  assert.equal(await state(page, ".preview", "type"), "password");
  await click(page, "#toggle-preview");
  assert.equal(await state(page, ".preview", "type"), "text");
  assert.equal(await state(page, "#toggle-preview", "title"), "Ocultar senha");
  await click(page, "#toggle-preview");
  assert.equal(await state(page, ".preview", "type"), "password");
  await click(page, "#regenerate");
  assert.equal((await state(page, ".preview", "value")).length, 20);
  await (await part(page, "details summary")).click();
  const length = await part(page, "#length"); await length.fill("28"); await length.press("Tab"); await ready(page);
  await page.waitForFunction(() => __testRoot.querySelector(".preview").value.length === 28);
  const generated = await state(page, ".preview", "value");
  assert.equal(await page.evaluate(() => __options.length), 28);
  await click(page, "#use");
  assert.equal(await page.locator("#password").inputValue(), generated);
  assert.equal(await page.locator("#confirm").inputValue(), generated);
  await (await part(page, "#save-target")).selectOption("account1");
  await click(page, "#save");
  assert.deepEqual(await page.evaluate(() => __saved[0]), { id: "account1", kind: "login", username: "novo@cofre.test", password: generated });
  await (await part(page, "#save-target")).selectOption("new-token");
  await click(page, "#save");
  assert.deepEqual(await page.evaluate(() => __saved[1]), { id: "", kind: "token", username: "novo@cofre.test", password: generated });
  await mkdir("test-results", { recursive: true });
  await page.screenshot({ path: "test-results/signup.png" });
});
test("real DOM: password-change preserves current password", { skip: !enabled }, async t => {
  const page = await setup(t, '<h1>Alterar senha</h1><form><input type="password" id="old" autocomplete="current-password" value="old-secret"><input type="password" id="password" autocomplete="new-password"><input type="password" id="confirm" autocomplete="new-password"></form>');
  await openFor(page, "#password");
  assert.match(await state(page, ".hint"), /Troca de senha/);
  const generated = await state(page, ".preview", "value");
  await click(page, "#use");
  assert.equal(await page.locator("#old").inputValue(), "old-secret");
  assert.equal(await page.locator("#password").inputValue(), generated);
  assert.equal(await page.locator("#confirm").inputValue(), generated);
});
test("real DOM: multiple forms target only the chosen form; dynamically inserted fields get icons", { skip: !enabled }, async t => {
  const page = await setup(t, login + '<form id="second"><input type="email" id="second-user"><input type="password" id="second-password"></form>');
  await openFor(page, "#second-password"); await click(page, "#entries button");
  assert.equal(await page.locator("#second-password").inputValue(), "test-only-secret");
  assert.equal(await page.locator("#password").inputValue(), "");
  await page.evaluate(() => {
    const field = document.createElement("input"); field.type = "password"; field.id = "dynamic";
    document.body.append(field);
  });
  await page.waitForFunction(() => __cofreInlineFields.size === 3);
  assert.equal(await page.evaluate(() => __testRoot.querySelectorAll(".field").length), 3);
  await page.evaluate(() => document.querySelector("#dynamic").remove());
  await page.waitForFunction(() => __cofreInlineFields.size === 2);
  await page.addScriptTag({ content: source });
  assert.equal(await page.locator("cofre-assistant").count(), 1);
});
test("real DOM: cross-origin actions, altered fields and mismatched confirmations fail closed", { skip: !enabled }, async t => {
  const page = await setup(t, signup);
  await openFor(page, "#password");
  await page.locator("#password").fill("one"); await page.locator("#confirm").fill("two");
  await page.mouse.click(30, 30);
  await openFor(page, "#password");
  await click(page, "#save"); assert.match(await state(page, ".status"), /precisam ser iguais/);
  assert.equal(await page.evaluate(() => __saved.length), 0);
  await page.evaluate(() => { document.querySelector("form").action = "https://evil.test"; });
  await click(page, "#regenerate"); assert.match(await state(page, ".status"), /outro domínio/);
});
test("real DOM: panel clears suggested password on outside click and Escape", { skip: !enabled }, async t => {
  const page = await setup(t, signup);
  await openFor(page, "#password");
  assert.ok(await state(page, ".preview", "value"));
  await page.mouse.click(30, 30);
  assert.equal(await state(page, ".panel", "hidden"), true);
  assert.equal(await state(page, ".preview", "value"), "");
  await openFor(page, "#password");
  await page.keyboard.press("Escape"); assert.equal(await state(page, ".panel", "hidden"), true);
});

test("real DOM: field icon leaves room for a wide show-password button", { skip: !enabled }, async t => {
  const page = await setup(t, '<h1>Entrar</h1><form><div style="position:relative"><input id="password" type="password"><button id="eye" type="button" style="position:absolute;right:0;top:0;height:46px;width:70px">Olho</button></div></form>');
  const positions = await page.evaluate(() => {
    const field = __testRoot.querySelector(".field").getBoundingClientRect();
    const eye = document.querySelector("#eye").getBoundingClientRect();
    return { iconRight: field.right, eyeLeft: eye.left };
  });
  assert.ok(positions.iconRight <= positions.eyeLeft - 5);
});

test("real DOM: invalidated extension asks to reload the page", { skip: !enabled }, async t => {
  const page = await setup(t, login);
  await openFor(page, "#password");
  await page.evaluate(() => { delete chrome.runtime; });
  await click(page, "#generator-tab");
  assert.match(await state(page, ".status"), /extensão foi atualizada.*Recarregue esta página/i);
  assert.equal(await state(page, ".preview", "value"), "");
});
