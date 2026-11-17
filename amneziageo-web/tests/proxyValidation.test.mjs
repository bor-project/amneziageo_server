import test from "node:test"
import assert from "node:assert/strict"
import { readFile } from "node:fs/promises"
import { execFileSync } from "node:child_process"
import { fileURLToPath } from "node:url"

const root = fileURLToPath(new URL("../", import.meta.url))
execFileSync(`${root}/node_modules/.bin/tsc`, ["-p", "tsconfig.app.json", "--noEmit", "false", "--allowImportingTsExtensions", "false",
  "--rootDir", "src", "--outDir", "node_modules/.tmp/proxy-tests", "--incremental", "true", "--tsBuildInfoFile", "node_modules/.tmp/proxy-tests.tsbuildinfo"], { cwd: root, stdio: "inherit" })
const code = await readFile(new URL("../node_modules/.tmp/proxy-tests/components/proxyValidation.js", import.meta.url), "utf8")
const { proxyHost, proxyFaults } = await import(`data:text/javascript;base64,${Buffer.from(code).toString("base64")}`)
const draft = { name: "Relay", transport: "quic", host: "proxy.example.net", port: 443, listenPort: 0, path: "relay", hosted: true,
  originHttp: false, useCertificate: true, httpMethod: "GET", protocolVersion: 0, isEnabled: true,
  allowAnonymous: false, opened: false, certificate: "", certificateKey: "", serverName: "", pin: "",
  targets: { restricted: true, local: true, destinations: [] } }
const faults = (changes = {}, all = []) => proxyFaults((key) => key, { ...draft, ...changes }, 0, all, "")

test("DNS, IPv4, IPv6 and IDN hosts remain valid", () => {
  for (const host of ["proxy.example.net", "localhost", "192.0.2.1", "2001:db8::1", "[2001:db8::1]", "пример.рф"]) { assert.equal(proxyHost(host), true, host) }
})
test("schemes, paths, credentials, wildcard and port do not become hosts", () => {
  for (const host of ["", "https://proxy.example.net", "proxy.example.net/path", "name@proxy.example.net", "*.example.net", "proxy.example.net:443", "[::1", "a..example.net", "foo bar", "-bad.example"]) { assert.equal(proxyHost(host), false, host) }
})
test("one public port remains available across interfaces and proxy protocols", () => {
  assert.equal(Object.values(faults({}, [{ ...draft, id: 1, name: "Other", path: "other", transport: "websocket" }])).every((one) => one === ""), true)
})
test("public port and inherited server port have different zero rules", () => {
  assert.equal(faults().listenPort, "")
  for (const port of [0, -1, 65536, 1.5, NaN, Infinity]) { assert.equal(faults({ port }).port, "error.badPort") }
  for (const listenPort of [-1, 65536, 1.5, NaN, Infinity]) { assert.equal(faults({ listenPort }).listenPort, "error.badPort") }
})
test("names, TLS identity and certificate fingerprint identify their fields", () => {
  assert.equal(faults({ name: "x".repeat(65) }).name, "error.proxyName")
  assert.equal(faults({ name: "line\nfeed" }).name, "error.proxyName")
  assert.equal(faults({}, [{ ...draft, id: 1, name: "RELAY" }]).name, "error.nameTaken")
  assert.equal(faults({ serverName: "https://proxy.example.net" }).serverName, "error.proxyHost")
  assert.equal(faults({ pin: "ab".repeat(32) }).pin, "")
  assert.equal(faults({ pin: "z".repeat(64) }).pin, "error.proxyPin")
  assert.equal(faults({ path: "API" }).path, "error.badWebSocketPath")
})
test("invalid destinations and contradictory local policy are rejected", () => {
  assert.equal(faults({ targets: { restricted: true, destinations: [] } }).targets, "error.proxyTargets")
  assert.equal(faults({ targets: { restricted: true, destinations: [{ host: "https://example.net", port: 51820 }] } }).targets, "error.proxyTargets")
  assert.equal(faults({ targets: { restricted: false, local: true, destinations: [] } }).targets, "error.proxyTargets")
  assert.equal(faults({ targets: { restricted: true, destinations: [{ host: "2001:db8::1", port: 51820 }] } }).targets, "")
})
test("legacy insecure or unsupported transports cannot be saved accidentally", () => {
  assert.equal(faults({ useCertificate: false }).certificate, "error.proxyTlsRequired")
  assert.equal(faults({ originHttp: true }).certificate, "error.proxyTlsRequired")
  assert.equal(faults({ transport: "xhttp" }).transport, "error.proxyTransport")
  assert.equal(faults({ transport: "http", protocolVersion: 2, httpMethod: "POST" }).transport, "")
})
