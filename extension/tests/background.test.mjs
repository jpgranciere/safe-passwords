import test from "node:test";
import assert from "node:assert/strict";

let listener;
let page = { id: 12, url: "https://example.com/login" };
let nativeResponse;
let nativeRequests = [], injections = [];
let changePageDuringConsent = false;
const caller = { id: "test-id", url: "chrome-extension://test-id/popup.html" };
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
  tabs: { query: async () => [page], get: async () => page },
  scripting: { executeScript: async options => { injections.push(options); return [{ result: { filled: true } }]; } },
  action: { setBadgeText() {}, setBadgeBackgroundColor() {}, setTitle() {} }
};
await import("../background.js");
async function request(message, sender = caller) {
  const result = await new Promise(resolve => {
    const keepAlive = listener(message, sender, resolve);
    if (!keepAlive && sender !== caller) resolve(null);
  });
  await new Promise(resolve => setImmediate(resolve));
  return result;
}
function reset() {
  page = { id: 12, url: "https://example.com/login" };
  nativeResponse = { ok: true, origin: "https://example.com", username: "demo", password: "test-only" };
  changePageDuringConsent = false; nativeRequests = []; injections = [];
}
test("fills through the worker after native consent and returns no secrets to popup", async () => {
  reset();
  const result = await request({ action: "fill", pageUrl: page.url, id: "entry-id" });
  assert.deepEqual(result, { ok: true }); assert.equal(injections.length, 1);
  assert.equal(nativeRequests[0].url, "https://example.com");
  assert.deepEqual(injections[0].args, [page.url, "demo", "test-only"]);
});
test("does not inject after cancellation or navigation during consent", async () => {
  reset(); nativeResponse = { ok: false, error: "Cancelado" };
  assert.equal((await request({ action: "fill", pageUrl: page.url, id: "id" })).ok, false);
  assert.equal(injections.length, 0);
  reset(); changePageDuringConsent = true;
  assert.equal((await request({ action: "fill", pageUrl: page.url, id: "id" })).ok, false);
  assert.equal(injections.length, 0);
});
test("rejects responses for another origin and stale popup context", async () => {
  reset(); nativeResponse.origin = "https://evil.test";
  assert.equal((await request({ action: "fill", pageUrl: page.url, id: "id" })).ok, false);
  assert.equal(injections.length, 0);
  reset(); assert.equal((await request({ action: "fill", pageUrl: "https://example.com/old", id: "id" })).ok, false);
  assert.equal(nativeRequests.length, 0);
});
test("ignores requests from content scripts and websites", async () => {
  reset(); assert.equal(await request({ action: "fill" }, { id: "test-id", url: page.url }), null);
  assert.equal(nativeRequests.length, 0);
});
test("refuses non-HTTPS pages", async () => {
  reset(); page.url = "http://example.com";
  assert.equal((await request({ action: "list" })).ok, false); assert.equal(nativeRequests.length, 0);
});
