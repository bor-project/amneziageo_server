import test from "node:test"
import assert from "node:assert/strict"
import { readFile } from "node:fs/promises"
import { execFileSync } from "node:child_process"
import { fileURLToPath } from "node:url"

const root = fileURLToPath(new URL("../", import.meta.url))
execFileSync(`${root}/node_modules/.bin/tsc`, ["-p", "tsconfig.app.json", "--noEmit", "false", "--allowImportingTsExtensions", "false",
  "--rootDir", "src", "--outDir", "node_modules/.tmp/change-tests", "--incremental", "true", "--tsBuildInfoFile", "node_modules/.tmp/change-tests.tsbuildinfo"], { cwd: root, stdio: "inherit" })
const code = await readFile(new URL("../node_modules/.tmp/change-tests/components/configChangeFields.js", import.meta.url), "utf8")
const { configChangeField } = await import(`data:text/javascript;base64,${Buffer.from(code).toString("base64")}`)

test("preview fields use the labels of the interface form", () => {
  assert.equal(configChangeField(key => key, "[interface].mtu"), "configs.mtu")
  assert.equal(configChangeField(key => key, "[peer]1.publickey"), "configs.public")
  assert.equal(configChangeField(key => key, "[interface].HeaderProtectionKey"), "configs.headerKey")
})

test("service metadata uses a label instead of the raw comment", () => {
  assert.equal(configChangeField(key => key, "[interface].# AmneziaGeo Services"), "configs.servicesPort")
})

test("future field names preserve their identity without a parser prefix", () => {
  assert.equal(configChangeField(key => key, "[interface].FutureField"), "FutureField")
  assert.equal(configChangeField(key => key, "Configuration"), "Configuration")
})
