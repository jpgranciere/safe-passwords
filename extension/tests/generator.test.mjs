import test from "node:test";
import assert from "node:assert/strict";
import { defaults, generatePassword, validateOptions } from "../generator.js";

test("generator uses configured length and every selected character class", () => {
  for (let i = 0; i < 100; i++) {
    const password = generatePassword(defaults);
    assert.equal(password.length, 20);
    assert.match(password, /[A-Z]/); assert.match(password, /[a-z]/); assert.match(password, /[0-9]/);
    assert.ok([...password].some(c => defaults.symbolChars.includes(c)));
    assert.doesNotMatch(password, /[O0Il1|]/);
  }
});
test("custom symbols and disabled classes are respected", () => {
  const options = { ...defaults, length: 32, upper: false, digits: false, symbolChars: "!?" };
  for (let i = 0; i < 30; i++) assert.match(generatePassword(options), /^[a-z!?]{32}$/);
  assert.equal(generatePassword({ ...defaults, length: 128 }).length, 128);
  assert.equal(new Set(Array.from({ length: 50 }, () => generatePassword(defaults))).size, 50);
});
test("invalid lengths, classes and symbol injection are rejected", () => {
  for (const length of [4, 11, 129, 20.5, "20", NaN]) assert.throws(() => generatePassword({ length }));
  assert.throws(() => generatePassword({ upper: false, lower: false, digits: false, symbols: false }));
  for (const symbolChars of ["", "abc", "<script>", " ", "😀"]) assert.throws(() => generatePassword({ symbolChars }));
  assert.throws(() => generatePassword({ symbolChars: "|", upper: false, lower: false, digits: false }));
  assert.throws(() => validateOptions({ upper: "true" }));
  assert.deepEqual(validateOptions({ ...defaults, password: "must-not-persist" }), defaults);
});
