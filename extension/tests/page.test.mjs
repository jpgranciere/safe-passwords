import test from "node:test";
import assert from "node:assert/strict";
import { readLogin, fillLogin } from "../page.js";

const url = "https://example.com/login";
class Input {
  constructor(type, order) { this.type = type; this.order = order; this.isConnected = true; this.autocomplete = ""; this._value = ""; this.events = []; }
  get value() { return this._value; }
  set value(v) { this._value = v; }
  getClientRects() { return this.hidden ? [] : [{}]; }
  compareDocumentPosition(other) { return this.order < other.order ? 4 : 2; }
  dispatchEvent(event) { this.events.push(event.type); this.onEvent?.(event); }
}
function setup() {
  const user = new Input("email", 1), password = new Input("password", 2), hidden = new Input("password", 3);
  hidden.hidden = true;
  const fields = [user, password, hidden];
  const form = { action: url, querySelectorAll: () => fields };
  fields.forEach(f => { f.form = form; });
  globalThis.window = {}; window.top = window;
  globalThis.location = { href: url, origin: "https://example.com", protocol: "https:" };
  globalThis.Node = { DOCUMENT_POSITION_FOLLOWING: 4 };
  globalThis.HTMLInputElement = Input;
  globalThis.getComputedStyle = () => ({ visibility: "visible", display: "block" });
  globalThis.document = { querySelectorAll: () => fields.filter(f => f.type === "password") };
  return { user, password, hidden, fields, form };
}

test("fills the visible login, dispatches framework events and never submits", () => {
  const { user, password, hidden } = setup();
  assert.deepEqual(fillLogin(url, "demo@example.com", "test-only"), { filled: true });
  assert.equal(user.value, "demo@example.com"); assert.equal(password.value, "test-only");
  assert.equal(hidden.value, ""); assert.deepEqual(password.events, ["input", "change"]);
});
test("reads credentials only after an explicit request", () => {
  const { user, password } = setup(); user.value = "demo"; password.value = "test-only";
  assert.deepEqual(readLogin(url), { username: "demo", password: "test-only" });
});
test("rejects navigation, HTTP and child frames", () => {
  setup(); location.href = "https://example.com/new"; assert.throws(() => fillLogin(url, "u", "p"));
  setup(); location.protocol = "http:"; assert.throws(() => readLogin(url));
  setup(); window.top = {}; assert.throws(() => fillLogin(url, "u", "p"));
});
test("rejects cross-origin form actions for both fill and read", () => {
  const { form, password } = setup(); form.action = "https://evil.test/collect";
  assert.throws(() => fillLogin(url, "u", "p")); assert.throws(() => readLogin(url)); assert.equal(password.value, "");
});
test("rejects ambiguous passwords and new-password fields", () => {
  let fixture = setup(); fixture.hidden.hidden = false;
  assert.throws(() => fillLogin(url, "u", "p"));
  fixture = setup(); fixture.password.autocomplete = "new-password";
  assert.throws(() => fillLogin(url, "u", "p"));
});
test("rechecks the form after username events before releasing password", () => {
  const { form, user, password } = setup();
  user.onEvent = () => { form.action = "https://evil.test/collect"; };
  assert.throws(() => fillLogin(url, "u", "secret")); assert.equal(password.value, "");
});
test("does not write into a password field removed by a framework", () => {
  const { user, password } = setup(); user.onEvent = () => { password.isConnected = false; };
  assert.throws(() => fillLogin(url, "u", "secret")); assert.equal(password.value, "");
});
