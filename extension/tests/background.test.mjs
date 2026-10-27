import test from "node:test";
import assert from "node:assert/strict";
let listener;
let page, nativeResponse, nativeRequests, injections, changePageDuringConsent, inspection, savedSettings, permission = true;
const caller = { id: "test-id", url: "chrome-extension://test-id/popup.html" };
const inline = () => ({ id: "test-id", url: page.url, origin: new URL(page.url).origin, frameId: 0,
  tab: { id: page.id }, documentId: "test-document" });
globalThis.chrome = {
  runtime: {
    id: "test-id", getURL: path => "chrome-extension://test-id/" + path,
    onMessage: { addListener: fn => { listener = fn; } },
    connectNative: () => {
      let onMessage, onDisconnect;
      return {
        onMessage: { addListener: fn => { onMessage = fn; } },
        onDisconnect: { addListener: fn => { onDisconnect = fn; } },
        disconnect: () => { onDisconnect?.(); },
        postMessage: request => {
          nativeRequests.push(request);
          queueMicrotask(() => {
            if (changePageDuringConsent) page = { ...page, url: "https://evil.test" };
            onMessage(nativeResponse);
          });
        }
      };
    }
  },
  storage: { local: { setAccessLevel: async () => {}, get: async () => ({ generatorOptions: savedSettings }),
    set: async value => { savedSettings = value.generatorOptions; } } },
  permissions: { getAll: async () => ({ origins: [] }), contains: async () => permission,
    onAdded: { addListener() {} }, onRemoved: { addListener() {} } },
  tabs: { query: async () => [page], get: async () => page },
  scripting: { getRegisteredContentScripts: async () => [], registerContentScripts: async () => {}, unregisterContentScripts: async () => {}, executeScript: async options => {
    injections.push(options);
    const action = options.args?.[1];
    return [{ result: action === "inspect" ? inspection : action === "read" ? { username: "demo", password: "form-secret" } : { filled: true } }];
  } },
  action: { setBadgeText() {}, setBadgeBackgroundColor() {}, setTitle() {} }
};
await import("../background.js");
async function request(message, sender = caller) {
  const result = await new Promise(resolve => {
    const keepAlive = listener(message, sender, resolve);
    if (!keepAlive) resolve(null);
  });
  await new Promise(resolve => setImmediate(resolve));
  return result;
}
function reset() {
  page = { id: 12, url: "https://example.com/login", active: true };
  nativeResponse = { ok: true, origin: "https://example.com", username: "demo", password: "test-only", entries: [] };
  changePageDuringConsent = false; nativeRequests = []; injections = [];
  inspection = { kind: "login", minLength: 0, maxLength: 128 }; savedSettings = undefined;
  permission = true;
}
test("fills through worker after consent and returns no secrets to popup", async () => {
  reset();
  assert.deepEqual(await request({ action: "fill", pageUrl: page.url, id: "entry-id" }), { ok: true });
  assert.equal(nativeRequests[0].url, "https://example.com");
  assert.deepEqual(injections.at(-1).args, [page.url, "fill", { username: "demo", password: "test-only" }]);
});
test("cancellation, navigation and foreign-origin responses never inject passwords", async () => {
  for (const failure of ["cancel", "navigation", "origin"]) {
    reset();
    if (failure === "cancel") nativeResponse = { ok: false, error: "Cancelado" };
    if (failure === "navigation") changePageDuringConsent = true;
    if (failure === "origin") nativeResponse.origin = "https://evil.test";
    assert.equal((await request({ action: "fill", pageUrl: page.url, id: "id" })).ok, false);
    assert.equal(injections.filter(i => i.args?.[1] === "fill").length, 0);
  }
});
test("rejects stale popup and non-HTTPS context", async () => {
  reset(); assert.equal((await request({ action: "fill", pageUrl: "https://example.com/old", id: "id" })).ok, false);
  assert.equal(nativeRequests.length, 0);
  page.url = "http://example.com";
  assert.equal((await request({ action: "list" })).ok, false); assert.equal(nativeRequests.length, 0);
});
test("ignores websites, foreign extensions and child frames", async () => {
  reset();
  for (const sender of [{ id: "test-id", url: page.url }, { ...inline(), id: "foreign" },
    { ...inline(), frameId: 1 }, { ...inline(), origin: "https://evil.test" }])
    assert.equal(await request({ action: "inline.fill", pageUrl: page.url, field: "token" }, sender), null);
  assert.equal(nativeRequests.length, 0);
});
test("top-frame inline request pins injection to sender document and never receives a vault password", async () => {
  reset();
  const response = await request({ action: "inline.fill", pageUrl: page.url, field: "random-token", id: "id" }, inline());
  assert.deepEqual(response, { ok: true });
  assert.deepEqual(injections.at(-1).target, { tabId: 12, documentIds: ["test-document"] });
  assert.equal(injections.at(-1).args[2].field, "random-token");
});
test("inline cannot impersonate popup save/read or change its site context", async () => {
  reset();
  for (const message of [{ action: "read" }, { action: "save", password: "fake" },
    { action: "inline.list", pageUrl: "https://evil.test", field: "token" }])
    assert.equal((await request({ ...message, pageUrl: page.url, ...message }, inline())).ok, false);
  assert.equal(nativeRequests.length, 0);
});
test("background refuses to release stored credentials into registration forms", async () => {
  reset(); inspection.kind = "signup";
  assert.equal((await request({ action: "inline.fill", pageUrl: page.url, field: "token", id: "id" }, inline())).ok, false);
  assert.equal(nativeRequests.length, 0);
});
test("inline save reads fresh form values in isolated world and updates only selected account", async () => {
  reset();
  await request({ action: "inline.save", pageUrl: page.url, field: "token", id: "account-id", password: "spoof" }, inline());
  assert.deepEqual(nativeRequests[0], { action: "update", url: "https://example.com", id: "account-id",
    title: "example.com", username: "demo", password: "form-secret" });
});
test("generation stores only preferences and requires no native connection", async () => {
  reset();
  const response = await request({ action: "inline.generate", pageUrl: page.url, field: "token", options: { length: 24 } }, inline());
  assert.equal(response.password.length, 24); assert.equal(savedSettings.length, 24);
  assert.equal(Object.hasOwn(savedSettings, "password"), false); assert.equal(nativeRequests.length, 0);
  assert.equal((await request({ action: "inline.generate", pageUrl: page.url, field: "token", options: { length: 4 } }, inline())).ok, false);
});
test("inline requests from inactive or navigated tabs are rejected", async () => {
  reset(); const sender = inline(); page.url = "https://example.com/new";
  assert.equal((await request({ action: "inline.list", pageUrl: sender.url, field: "token" }, sender)).ok, false);
  reset(); page.active = false;
  assert.equal((await request({ action: "inline.list", pageUrl: page.url, field: "token" }, inline())).ok, false);
  assert.equal(nativeRequests.length, 0);
});
test("removing site permission stops existing inline scripts from requesting data", async () => {
  reset(); permission = false;
  assert.equal((await request({ action: "inline.list", pageUrl: page.url, field: "token" }, inline())).ok, false);
  assert.equal(nativeRequests.length, 0); assert.equal(injections.length, 0);
});

test("site activation remains available during native consent without unlocking concurrent credential requests", async () => {
  reset();
  const connect = chrome.runtime.connectNative;
  let release;
  chrome.runtime.connectNative = () => {
    let onMessage, onDisconnect;
    return { onMessage: { addListener: fn => { onMessage = fn; } },
      onDisconnect: { addListener: fn => { onDisconnect = fn; } },
      disconnect: () => onDisconnect?.(), postMessage: request => { nativeRequests.push(request); release = () => onMessage(nativeResponse); } };
  };
  try {
    const pending = request({ action: "fill", pageUrl: page.url, id: "id" });
    await new Promise(resolve => setImmediate(resolve));
    const result = await request({ action: "attach", pageUrl: page.url });
    assert.equal(result.ok, true);
    assert.ok(injections.some(item => item.files?.includes("content.js")));
    const duplicate = await request({ action: "list" });
    assert.equal(duplicate.ok, false); assert.match(duplicate.error, /em andamento/);
    release(); assert.equal((await pending).ok, true);
  } finally { chrome.runtime.connectNative = connect; }
});
