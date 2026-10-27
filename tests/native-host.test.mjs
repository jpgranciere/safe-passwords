import test from "node:test";
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { createServer } from "node:net";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { createHash } from "node:crypto";

const host = process.env.COFRE_NATIVE_HOST;
const manifest = host ? JSON.parse(readFileSync(join(dirname(host), "com.cofre.passwords.json"), "utf8")) : null;
const caller = manifest?.allowed_origins[0];
const frame = value => {
  const body = Buffer.from(JSON.stringify(value)); const size = Buffer.alloc(4); size.writeInt32LE(body.length);
  return Buffer.concat([size, body]);
};
function run(origin, bytes) {
  return new Promise((resolve, reject) => {
    const child = spawn(host, [origin], { windowsHide: true, stdio: ["pipe", "pipe", "pipe"] });
    const chunks = [], errors = [];
    child.stdout.on("data", b => chunks.push(b)); child.stderr.on("data", b => errors.push(b));
    child.on("error", reject);
    const timeout = setTimeout(() => { child.kill(); reject(new Error("Native host test timed out")); }, 10000);
    child.stdin.on("error", () => {}); child.stdin.end(bytes);
    child.on("exit", code => {
      clearTimeout(timeout);
      try {
        const output = Buffer.concat(chunks);
        assert.equal(code, 0); assert.equal(Buffer.concat(errors).length, 0);
        assert.equal(output.readInt32LE(0), output.length - 4);
        resolve(JSON.parse(output.subarray(4).toString("utf8")));
      } catch (error) { reject(error); }
    });
  });
}
test("native executable rejects an unauthorized extension", { skip: !host }, async () => {
  const response = await run("chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/", frame({ action: "list" }));
  assert.equal(response.ok, false);
});
test("native executable rejects oversized frames", { skip: !host }, async () => {
  const oversized = Buffer.alloc(4); oversized.writeInt32LE(65537);
  assert.equal((await run(caller, oversized)).ok, false);
});
test("native executable relays the registered caller through the current-user pipe", { skip: !host }, async () => {
  const identity = process.env.COFRE_TEST_IDENTITY;
  assert.ok(identity, "Set COFRE_TEST_IDENTITY to Environment.UserDomainName + '\\' + Environment.UserName.");
  const suffix = createHash("sha256").update(identity).digest("hex").slice(0, 20).toUpperCase();
  const pipe = "\\\\.\\pipe\\Cofre.Browser." + suffix;
  let received;
  const server = createServer(socket => {
    let data = Buffer.alloc(0);
    socket.on("data", bytes => {
      data = Buffer.concat([data, bytes]);
      if (data.length < 4 || data.length < data.readInt32LE(0) + 4) return;
      received = JSON.parse(data.subarray(4).toString("utf8"));
      socket.end(frame({ ok: true, entries: [] }));
    });
  });
  await new Promise((resolve, reject) => { server.once("error", reject); server.listen(pipe, resolve); });
  try {
    assert.deepEqual(await run(caller, frame({ action: "list", url: "https://example.com", extensionOrigin: "forged" })), { ok: true, entries: [] });
    assert.equal(received.extensionOrigin, caller); assert.equal(received.action, "list");
  } finally { await new Promise(resolve => server.close(resolve)); }
});
