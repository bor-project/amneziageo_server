import test from "node:test"
import assert from "node:assert/strict"
import { readFile } from "node:fs/promises"
import { execFileSync } from "node:child_process"
import { fileURLToPath } from "node:url"

const root = fileURLToPath(new URL("../", import.meta.url))
execFileSync(`${root}/node_modules/.bin/tsc`, ["-p", "tsconfig.app.json", "--noEmit", "false", "--allowImportingTsExtensions", "false",
  "--rootDir", "src", "--outDir", "node_modules/.tmp/dns-policy-tests", "--incremental", "true", "--tsBuildInfoFile", "node_modules/.tmp/dns-policy-tests.tsbuildinfo"], { cwd: root, stdio: "inherit" })
const code = await readFile(new URL("../node_modules/.tmp/dns-policy-tests/components/dnsPolicy.js", import.meta.url), "utf8")
const { dnsChoice, dnsChosen, dnsPolicy, dnsPresets } = await import(`data:text/javascript;base64,${Buffer.from(code).toString("base64")}`)
const rule = { name: "Sites", targets: ["example.com"], dnsMode: "default", dns: [], dnsMembers: {} }
const server = (name, dns, carries = true) => ({ name, kind: "wg", dns, isEnabled: true, state: { carries } })
const servers = [server("first", ["10.8.0.1"]), server("second", ["10.9.0.1"])]
const balancer = (strategy = "priority", changes = {}) => ({ name: "pool", isEnabled: true, strategy, members: ["first", "second"], dns: [],
  state: { members: servers.map(one => ({ name: one.name, isEnabled: true, isAlive: true })) }, ...changes })
const selected = (rule, name, ways = servers, groups = []) => dnsPolicy(rule, { action: "out", outbound: name }, ways, groups, ["192.168.1.1"])

test("default DNS follows current linked-server metadata and ignores stale explicit overrides", () => {
  const held = { ...rule, dns: ["8.8.8.8"], dnsMembers: { first: ["9.9.9.9"] } }
  assert.deepEqual(selected(held, "first").options[0].servers, ["10.8.0.1"])
  assert.deepEqual(selected(held, "second").options[0].servers, ["10.9.0.1"])
  assert.deepEqual(selected(held, "first", [server("first", ["10.10.0.1"])]).options[0].servers, ["10.10.0.1"])
})

test("direct DNS comes from host metadata and missing sources do not invent a public provider", () => {
  assert.deepEqual(dnsPolicy(rule, { action: "direct", outbound: "" }, [], [], ["192.168.5.1"]).options[0].servers, ["192.168.5.1"])
  assert.equal(dnsPolicy(rule, { action: "direct", outbound: "" }, [], [], []).state, "missing")
  assert.equal(selected(rule, "first", [server("first", [])]).state, "missing")
})

test("priority uses the first live member and changes when that member becomes unavailable", () => {
  const pool = balancer()
  assert.deepEqual(selected(rule, "pool", servers, [pool]).options, [{ path: "first", servers: ["10.8.0.1"] }])
  const down = { ...pool, state: { members: [{ name: "first", isEnabled: true, isAlive: false }, { name: "second", isEnabled: true, isAlive: true }] } }
  assert.deepEqual(selected(rule, "pool", servers, [down]).options, [{ path: "second", servers: ["10.9.0.1"] }])
})

test("round and sticky retain every possible live path rather than claiming one active DNS", () => {
  for (const strategy of ["round", "sticky"]) {
    const answer = selected(rule, "pool", servers, [balancer(strategy)])
    assert.equal(answer.variant, strategy)
    assert.deepEqual(answer.options.map(one => one.path), ["first", "second"])
    assert.deepEqual(answer.options.map(one => one.servers), [["10.8.0.1"], ["10.9.0.1"]])
  }
})

test("balancer DNS overrides inherited member addresses while keeping all possible paths", () => {
  const answer = selected(rule, "pool", servers, [balancer("round", { dns: ["1.1.1.1"] })])
  assert.deepEqual(answer.options, [{ path: "first", servers: ["1.1.1.1"] }, { path: "second", servers: ["1.1.1.1"] }])
})

test("custom member overrides remain visible until a preset replaces and clears them", () => {
  const saved = { ...rule, dnsMode: "custom", dns: ["1.1.1.1", "1.0.0.1"], dnsMembers: { second: ["10.99.0.1"] } }
  assert.equal(dnsChoice(saved), "custom")
  assert.deepEqual(selected(saved, "pool", servers, [balancer("round")]).options[1].servers, ["10.99.0.1"])
  const preset = { ...saved, ...dnsChosen(saved, "google") }
  assert.deepEqual(preset.dnsMembers, {})
  assert.deepEqual(selected(preset, "pool", servers, [balancer("round")]).options[1].servers, ["8.8.8.8", "8.8.4.4"])
  assert.deepEqual(saved.dnsMembers, { second: ["10.99.0.1"] })
})

test("presets round-trip exact arrays and custom IPv6 addresses retain their identity", () => {
  for (const preset of dnsPresets) {
    const chosen = { ...rule, ...dnsChosen(rule, preset.key) }
    assert.equal(chosen.dnsMode, "custom")
    assert.deepEqual(chosen.dns, preset.servers)
    assert.equal(dnsChoice(chosen), preset.key)
  }
  const custom = { ...rule, dnsMode: "custom", dns: ["[2001:db8::53]:53", "tls://9.9.9.9"], dnsMembers: {} }
  assert.equal(dnsChoice(custom), "custom")
  assert.deepEqual(dnsChosen(custom, "custom").dns, custom.dns)
})

test("default mode does not retain hidden custom members after an explicit choice", () => {
  const custom = { ...rule, dnsMode: "custom", dns: ["9.9.9.9"], dnsMembers: { first: ["8.8.8.8"] } }
  assert.deepEqual(dnsChosen(custom, "default"), { dnsMode: "default", dns: [], dnsMembers: {} })
})

test("disabled and unavailable processing objects never preview DNS through a direct fallback", () => {
  const custom = { ...rule, dnsMode: "custom", dns: ["1.1.1.1"] }
  for (const current of [rule, custom]) {
    assert.equal(selected(current, "first", [server("first", ["10.8.0.1"], false)]).state, "unavailable")
    assert.equal(selected(current, "pool", servers, [balancer("round", { isEnabled: false })]).state, "unavailable")
    assert.equal(selected(current, "missing").state, "unavailable")
    assert.equal(dnsPolicy(current, { action: "block", outbound: "" }, servers, [], ["192.168.1.1"]).state, "blocked")
  }
})

test("legacy local paths inherit host DNS without becoming remote servers", () => {
  const local = { ...server("local", []), kind: "local" }
  assert.deepEqual(selected(rule, "local", [local]).options[0].servers, ["192.168.1.1"])
})
