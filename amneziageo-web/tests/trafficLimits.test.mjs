import test from "node:test"
import assert from "node:assert/strict"
import { readFile } from "node:fs/promises"
import { execFileSync } from "node:child_process"
import { fileURLToPath } from "node:url"

const root = fileURLToPath(new URL("../", import.meta.url))
execFileSync(`${root}/node_modules/.bin/tsc`, ["-p", "tsconfig.app.json", "--noEmit", "false", "--allowImportingTsExtensions", "false",
  "--rootDir", "src", "--outDir", "node_modules/.tmp/traffic-tests", "--incremental", "true", "--tsBuildInfoFile", "node_modules/.tmp/traffic-tests.tsbuildinfo"], { cwd: root, stdio: "inherit" })
const code = await readFile(new URL("../node_modules/.tmp/traffic-tests/components/trafficLimits.js", import.meta.url), "utf8")
const { trafficAmount, trafficAmountText, trafficLimitsInput, readTrafficLimits } = await import(`data:text/javascript;base64,${Buffer.from(code).toString("base64")}`)
const mbps = 1_000_000
const gib = 1024 ** 3
const policy = {
  enabled: true, uploadBitsPerSecond: 8, downloadBitsPerSecond: 123456789, quotaBytes: 3, period: "month",
  quotaAction: "throttle", exceededUploadBitsPerSecond: 999999, exceededDownloadBitsPerSecond: 987654321,
}
const read = (change, original = policy) => readTrafficLimits({ ...trafficLimitsInput(original), ...change }, original)

test("empty and zero limits explicitly allow unlimited traffic", () => {
  for (const value of ["", "0", "0.0", "0,0"]) {
    assert.equal(trafficAmount(value, mbps, 1e12), 0)
    assert.equal(trafficAmount(value, gib, 2 ** 50), 0)
  }
  const result = read({ quotaBytes: "", uploadBitsPerSecond: "", downloadBitsPerSecond: "" })
  assert.deepEqual(result.faults, {})
  assert.equal(result.value.enabled, true)
  assert.equal(result.value.quotaBytes, 0)
})

test("decimal commas convert Mbps and binary gigabytes to integer server units", () => {
  assert.equal(trafficAmount("1,5", mbps, 1e12), 1500000)
  assert.equal(trafficAmount("0.25", gib, 2 ** 50), 268435456)
  assert.equal(trafficAmount(" 2 ", mbps, 1e12), 2000000)
})

test("out of range and nonnumeric values never turn into unlimited", () => {
  for (const value of ["-1", "NaN", "Infinity", "1e3", "1.", ".5", "+1", "1 000", "0.00000001"]) {
    assert.equal(trafficAmount(value, mbps, 1e12), null, value)
  }
  assert.equal(trafficAmount("NaN", mbps, 1e12, NaN), null)
  assert.equal(trafficAmount("1000000", mbps, 1e12), 1e12)
  assert.equal(trafficAmount("1000000.0000000001", mbps, 1e12), null)
  assert.equal(trafficAmount("1048576", gib, 2 ** 50), 2 ** 50)
  assert.equal(trafficAmount("1048576.0000000001", gib, 2 ** 50), null)
})

test("editing a speed preserves the exact unchanged quota bytes", () => {
  const result = read({ uploadBitsPerSecond: "2.5" })
  assert.deepEqual(result.faults, {})
  assert.equal(result.value.quotaBytes, 3)
  assert.equal(result.value.downloadBitsPerSecond, 123456789)
  assert.equal(result.value.uploadBitsPerSecond, 2500000)
  assert.equal(trafficAmount(trafficAmountText(1, gib), gib, 2 ** 50, 1), 1)
})

test("a quota requires both reduced speeds only when throttling is selected", () => {
  assert.equal(read({ exceededUploadBitsPerSecond: "" }).faults.exceededUploadBitsPerSecond, "error.trafficReducedRate")
  assert.equal(read({ exceededDownloadBitsPerSecond: "0" }).faults.exceededDownloadBitsPerSecond, "error.trafficReducedRate")
  assert.deepEqual(read({ quotaAction: "block", exceededUploadBitsPerSecond: "-", exceededDownloadBitsPerSecond: "-" }).faults, {})
  assert.deepEqual(read({ quotaBytes: "", exceededUploadBitsPerSecond: "-", exceededDownloadBitsPerSecond: "-" }).faults, {})
})

test("disabling limits preserves valid saved values and ignores unfinished input", () => {
  const result = read({ enabled: false, quotaBytes: "-", uploadBitsPerSecond: "too fast" })
  assert.deepEqual(result.faults, {})
  assert.deepEqual(result.value, { ...policy, enabled: false })
})

test("period and exhaustion behavior must be recognized", () => {
  assert.equal(read({ period: "rolling" }).faults.period, "error.trafficPeriod")
  assert.equal(read({ quotaAction: "" }).faults.quotaAction, "error.trafficQuotaAction")
  for (const period of ["day", "week", "month"]) {
    assert.deepEqual(read({ period }).faults, {})
  }
})

test("positive speeds below one byte per second are rejected", () => {
  assert.equal(read({ uploadBitsPerSecond: "0.000007" }).faults.uploadBitsPerSecond, "error.trafficRate")
  assert.equal(read({ downloadBitsPerSecond: "0,000001" }).faults.downloadBitsPerSecond, "error.trafficRate")
  assert.equal(read({ exceededUploadBitsPerSecond: "0.000007" }).faults.exceededUploadBitsPerSecond, "error.trafficReducedRate")
  assert.deepEqual(read({ uploadBitsPerSecond: "0.000008", exceededUploadBitsPerSecond: "0.000008" }).faults, {})
})
