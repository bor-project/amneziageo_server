import test from "node:test"
import assert from "node:assert/strict"
import { readFile } from "node:fs/promises"
import { execFileSync } from "node:child_process"
import { fileURLToPath } from "node:url"

const root = fileURLToPath(new URL("../", import.meta.url))
execFileSync(`${root}/node_modules/.bin/tsc`, ["-p", "tsconfig.app.json", "--noEmit", "false", "--allowImportingTsExtensions", "false",
  "--rootDir", "src", "--outDir", "node_modules/.tmp/header-tests", "--incremental", "true", "--tsBuildInfoFile", "node_modules/.tmp/header-tests.tsbuildinfo"], { cwd: root, stdio: "inherit" })
const code = await readFile(new URL("../node_modules/.tmp/header-tests/components/headerProtection.js", import.meta.url), "utf8")
const { headerProtectionChange, headerProtectionMtu } = await import(`data:text/javascript;base64,${Buffer.from(code).toString("base64")}`)
const cover = Object.freeze({
  jc: 7, jmin: 50, jmax: 1000, s1: 40, s2: 133, s3: 0, s4: 0,
  h1: "100-109", h2: "200-209", h3: "300-309", h4: "400-409",
  i1: "", i2: "", i3: "", i4: "", i5: "", headerProtectionKey: "",
  contentPaddingAddition: "", rekeyAfterTime: "", rekeyTimeout: "", rejectAfterTime: "",
  keepaliveTimeout: "", maxHandshakeAttempts: "", randomTrailers: true, disableCookies: true,
})
const defaults = {
  contentPaddingAddition: "0", rekeyAfterTime: "120", rekeyTimeout: "5", rejectAfterTime: "180",
  keepaliveTimeout: "10", maxHandshakeAttempts: "18",
}
const change = (values = {}) => headerProtectionChange({ ...cover, ...values })
const sizes = (value) => [value.s1, value.s2, value.s3, value.s4]

test("generating a key repairs the reported zero paddings and fills engine defaults", () => {
  const value = change()
  assert.deepEqual(sizes(value), [40, 133, 12, 12])
  assert.match(value.headerProtectionKey, /^[A-Za-z0-9+/]{43}=$/)
  assert.equal(Buffer.from(value.headerProtectionKey, "base64").length, 32)
  for (const [field, expected] of Object.entries(defaults)) {
    assert.equal(value[field], expected, field)
  }
})

test("valid custom paddings and explicit timing ranges survive regeneration", () => {
  const custom = { s1: 20, s2: 100, s3: 13, s4: 20, contentPaddingAddition: "0", rekeyAfterTime: "100-120",
    rekeyTimeout: "3-5", rejectAfterTime: "170-180", keepaliveTimeout: "5-10", maxHandshakeAttempts: "12-18" }
  const value = change(custom)
  for (const [field, expected] of Object.entries(custom)) {
    assert.equal(value[field], expected, field)
  }
})

test("zero overrides stay explicit while whitespace-only optional fields use defaults", () => {
  const blank = Object.fromEntries(Object.keys(defaults).map((key) => [key, " \t "]))
  const zero = Object.fromEntries(Object.keys(defaults).map((key) => [key, "0"]))
  for (const [field, expected] of Object.entries(defaults)) {
    assert.equal(change(blank)[field], expected)
    assert.equal(change(zero)[field], "0")
  }
})

test("repairing a short initiation preserves the valid response padding", () => {
  assert.deepEqual(sizes(change({ s1: 0, s2: 68 })), [13, 68, 12, 12])
})

test("response repair stays inside the maximum padding at a handshake collision", () => {
  assert.deepEqual(sizes(change({ s1: 1224, s2: 1280 })), [1224, 1279, 12, 12])
  assert.deepEqual(sizes(change({ s1: 1224, s2: 2000 })), [1224, 1279, 12, 12])
})

test("padding boundaries never produce fractional, short, oversized or identical handshake packets", () => {
  const inputs = [-1, 0, 11, 12, 12.5, 13, 67, 68, 69, 1224, 1280, 1281, NaN, Infinity]
  for (const s1 of inputs) {
    for (const s2 of inputs) {
      const value = change({ s1, s2, s3: s1, s4: s2 })
      for (const size of sizes(value)) {
        assert.ok(Number.isInteger(size) && size >= 12 && size <= 1280)
      }
      assert.notEqual(value.s1 + 56, value.s2)
    }
  }
})

test("repeat generation replaces only the key after dependent fields are compatible", () => {
  const first = change()
  const second = headerProtectionChange({ ...cover, ...first })
  assert.notEqual(first.headerProtectionKey, second.headerProtectionKey)
  assert.deepEqual({ ...first, headerProtectionKey: "" }, { ...second, headerProtectionKey: "" })
})

test("enabling header protection generates a missing key and preserves an existing key", () => {
  const first = headerProtectionChange(cover, false)
  const again = headerProtectionChange({ ...cover, ...first }, false)
  assert.equal(Buffer.from(first.headerProtectionKey, "base64").length, 32)
  assert.deepEqual(again, first)
})

test("adding header protection lowers the standard MTU and preserves explicit MTU choices", () => {
  assert.equal(headerProtectionMtu(1420, ""), 1400)
  for (const mtu of [0, 1280, 1380, 1400, 1500]) {
    assert.equal(headerProtectionMtu(mtu, ""), mtu)
  }
  assert.equal(headerProtectionMtu(1420, "existing-key"), 1420)
})

test("key generation preserves signatures, packet types, flags and the original draft", () => {
  const original = Object.freeze({ ...cover, i1: "<r 64>", i5: "<t>", randomTrailers: false, disableCookies: false })
  const saved = { ...original }
  const result = { ...original, ...headerProtectionChange(original) }
  assert.deepEqual(original, saved)
  for (const field of ["jc", "jmin", "jmax", "h1", "h2", "h3", "h4", "i1", "i2", "i3", "i4", "i5", "randomTrailers", "disableCookies"]) {
    assert.equal(result[field], original[field], field)
  }
})
