import test from "node:test";
import assert from "node:assert/strict";
import { mkdir, mkdtemp, copyFile, cp, readFile, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
const executablePath = process.env.COFRE_TEST_EXTENSION_BROWSER;
const extensionPath = fileURLToPath(new URL("..", import.meta.url));
const fixture = body => `<!doctype html><html lang="pt-BR"><meta charset="utf-8"><title>Teste de integração Cofre</title><style>body{margin:70px auto;width:420px;background:#f4f3ed;color:#242a20;font:16px system-ui}h1{font-size:32px}label{display:block;margin:22px 0}input{display:block;width:100%;height:46px;padding:8px 70px 8px 12px;box-sizing:border-box;border:1px solid #bbc3af;background:#fbfaf5;font:16px system-ui}button{padding:12px;background:#4d613b;color:white;border:0}</style>${body}</html>`;
const wait = async fn => { for (let i = 0; i < 150; i++) { const result = await fn(); if (result) return result; await new Promise(r => setTimeout(r, 25)); } throw new Error("UI condition timed out"); };

test("loaded MV3 extension: isolated fields, generation, login, native save/update and navigation checks", { skip: !executablePath }, async t => {
  // The test copy grants only the fictitious fixture host. Production requests each host from the user.
  const testExtension = await mkdtemp(path.join(tmpdir(), "cofre-extension-test-"));
  t.after(() => rm(testExtension, { recursive: true, force: true }));
  for (const file of ["manifest.json", "background.js", "content.js", "page.js", "generator.js", "popup.html", "popup.js", "popup.css"])
    await copyFile(path.join(extensionPath, file), path.join(testExtension, file));
  await cp(path.join(extensionPath, "icons"), path.join(testExtension, "icons"), { recursive: true });
  const manifest = JSON.parse(await readFile(path.join(testExtension, "manifest.json"), "utf8"));
  manifest.host_permissions = ["https://cofre.test/*"];
  await writeFile(path.join(testExtension, "manifest.json"), JSON.stringify(manifest));
  const context = await chromium.launchPersistentContext("", { executablePath, headless: true,
    viewport: { width: 1100, height: 960 }, ignoreDefaultArgs: ["--disable-extensions"],
    args: [`--disable-extensions-except=${testExtension}`, `--load-extension=${testExtension}`] });
  t.after(() => context.close());
  const worker = context.serviceWorkers()[0] || await context.waitForEvent("serviceworker");
  assert.match(worker.url(), /^chrome-extension:\/\/ligdohljiagpoepikbojmfkfflenndlb\//);
  await wait(async () => await worker.evaluate(async () => (await chrome.scripting.getRegisteredContentScripts()).length === 1));
  // Replace only the transport inside our disposable worker, never the installed user's Native Host.
  await worker.evaluate(() => {
    globalThis.nativeRequests = []; globalThis.nativeDelay = 0;
    chrome.runtime.connectNative = () => {
      let messageListener, disconnectListener, closed = false;
      return {
        onMessage: { addListener: fn => { messageListener = fn; } },
        onDisconnect: { addListener: fn => { disconnectListener = fn; } },
        disconnect: () => { closed = true; disconnectListener?.(); },
        postMessage: request => {
          nativeRequests.push(request);
          const response = request.action === "list" ? { ok: true, entries: [{ id: "account1", title: "Minha conta de teste", username: "demo@cofre.test" }] }
            : request.action === "fill" ? { ok: true, origin: request.url, username: "demo@cofre.test", password: "fictional-secret-only" } : { ok: true };
          setTimeout(() => { if (!closed) messageListener(response); }, nativeDelay);
        }
      };
    };
  });
  let body = '<h1>Crie sua conta</h1><form><label>E-mail<input type="email" value="novo@cofre.test"></label><label>Senha<input id="password" type="password" autocomplete="new-password"></label><label>Confirme a senha<input id="confirm" type="password" autocomplete="new-password"></label><button>Cadastrar</button></form>';
  await context.route("https://cofre.test/**", route => route.fulfill({ body: fixture(body), contentType: "text/html" }));
  const page = await context.newPage();
  const cdp = await context.newCDPSession(page);
  async function element(selector) {
    const { root } = await cdp.send("DOM.getDocument", { depth: -1, pierce: true });
    function find(node) {
      if (node.nodeName === "COFRE-ASSISTANT") return node;
      for (const child of [...(node.children || []), ...(node.shadowRoots || [])]) { const found = find(child); if (found) return found; }
    }
    const host = find(root);
    assert.equal(host.shadowRoots[0].shadowRootType, "closed");
    const { nodeId } = await cdp.send("DOM.querySelector", { nodeId: host.shadowRoots[0].nodeId, selector });
    assert.ok(nodeId, selector);
    return nodeId;
  }
  async function property(selector, name) {
    const { object } = await cdp.send("DOM.resolveNode", { nodeId: await element(selector) });
    try { return (await cdp.send("Runtime.callFunctionOn", { objectId: object.objectId,
      functionDeclaration: "function(name) { return this[name]; }", arguments: [{ value: name }], returnByValue: true })).result.value; }
    finally { await cdp.send("Runtime.releaseObject", { objectId: object.objectId }); }
  }
  async function click(selector) {
    const { model } = await cdp.send("DOM.getBoxModel", { nodeId: await element(selector) });
    const q = model.content;
    await page.mouse.click((q[0] + q[4]) / 2, (q[1] + q[5]) / 2);
    await wait(async () => !await property("#save", "disabled"));
  }
  await page.goto("https://cofre.test/register");
  await page.waitForSelector("cofre-assistant", { state: "attached" });
  assert.equal(await page.evaluate(() => typeof globalThis.__cofreInlineFields), "undefined", "Field map must be isolated from site JavaScript");
  await page.locator("#password").click();
  await wait(async () => !(await property(".panel", "hidden")) && (await property(".preview", "value"))?.length === 20 && !await property("#save", "disabled"))
    .catch(async error => { throw new Error(error.message + ": " + await property(".status", "textContent")); });
  assert.match(await property(".hint", "textContent"), /Cadastro identificado/);
  await mkdir("test-results", { recursive: true });
  await page.screenshot({ path: "test-results/extension-signup.png" });
  const suggested = await property(".preview", "value");
  await click("#use");
  assert.equal(await page.locator("#password").inputValue(), suggested);
  assert.equal(await page.locator("#confirm").inputValue(), suggested);
  await click("#save");
  const saved = await worker.evaluate(() => nativeRequests.find(r => r.action === "save"));
  assert.equal(saved.password, suggested); assert.equal(saved.username, "novo@cofre.test"); assert.equal(saved.url, "https://cofre.test");
  assert.equal(await worker.evaluate(async () => Object.hasOwn(await chrome.storage.local.get(null), "password")), false);

  body = '<h1>Entre na sua conta</h1><form><label>E-mail<input type="email" autocomplete="username"></label><label>Senha<input id="password" type="password" autocomplete="current-password"></label><button>Entrar</button></form>';
  await page.goto("https://cofre.test/login"); await page.locator("#password").click();
  await wait(async () => (await property("#entries", "textContent"))?.includes("demo@cofre.test") && !await property("#save", "disabled"));
  assert.equal(await page.locator("#password").inputValue(), "", "Focus alone must not release a stored password");
  await page.screenshot({ path: "test-results/extension-login.png" });
  await click("#entries button");
  assert.equal(await page.locator("#password").inputValue(), "fictional-secret-only");
  assert.equal(await page.locator("input[type=email]").inputValue(), "demo@cofre.test");
  assert.equal(await property("#save-target", "value"), "account1");
  await click("#save");
  const updated = await worker.evaluate(() => nativeRequests.find(r => r.action === "update"));
  assert.equal(updated.id, "account1"); assert.equal(updated.password, "fictional-secret-only");

  await worker.evaluate(() => { globalThis.nativeDelay = 400; });
  // Navigation invalidates the document targeted by the pending fill request.
  const { model } = await cdp.send("DOM.getBoxModel", { nodeId: await element("#entries button") });
  const q = model.content; await page.mouse.click((q[0] + q[4]) / 2, (q[1] + q[5]) / 2);
  await wait(async () => (await worker.evaluate(() => nativeRequests.filter(r => r.action === "fill").length)) === 2);
  await page.goto("https://cofre.test/different-page");
  await wait(async () => await worker.evaluate(async () => (await chrome.action.getBadgeText({})) === "!"));
  assert.equal(await page.locator("#password").inputValue(), "");
});
