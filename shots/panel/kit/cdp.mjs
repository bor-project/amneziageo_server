// Drives one tab of a Chrome started with --remote-debugging-port over the DevTools protocol.
import { writeFileSync } from "node:fs"

export async function connect(port, origin) {
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()
  const target = targets.find((t) => t.type === "page" && t.url.startsWith(origin))
  if (!target) {
    throw new Error(`no tab of the stand in the Chrome on port ${port}`)
  }
  const ws = new WebSocket(target.webSocketDebuggerUrl)
  await new Promise((ok, fail) => {
    ws.onopen = ok
    ws.onerror = fail
  })
  let next = 1
  const waiting = new Map()
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data)
    if (m.id && waiting.has(m.id)) {
      const { ok, fail } = waiting.get(m.id)
      waiting.delete(m.id)
      if (m.error) {
        fail(new Error(`${m.error.message} ${m.error.data ?? ""}`))
      } else {
        ok(m.result)
      }
    }
  }
  const send = (method, params = {}) =>
    new Promise((ok, fail) => {
      const id = next++
      waiting.set(id, { ok, fail })
      ws.send(JSON.stringify({ id, method, params }))
    })
  const page = {
    send,
    close: () => ws.close(),
    async eval(expression) {
      const r = await send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true })
      if (r.exceptionDetails) {
        throw new Error(r.exceptionDetails.exception?.description ?? r.exceptionDetails.text)
      }
      return r.result.value
    },
    async waitFor(expression, timeout = 15000) {
      const end = Date.now() + timeout
      while (Date.now() < end) {
        if (await page.eval(`!!(${expression})`)) {
          return
        }
        await pause(200)
      }
      throw new Error(`timeout: ${expression}`)
    },
    async viewport(width, height, scale) {
      await send("Emulation.setDeviceMetricsOverride", { width, height, deviceScaleFactor: scale, mobile: false })
    },
    async shot(file, clip) {
      const params = { format: "png", captureBeyondViewport: clip !== undefined }
      if (clip) {
        params.clip = { ...clip, scale: 1 }
      }
      const r = await send("Page.captureScreenshot", params)
      writeFileSync(file, Buffer.from(r.data, "base64"))
    },
  }
  await send("Page.enable")
  await send("Runtime.enable")
  await send("Emulation.setFocusEmulationEnabled", { enabled: true })
  return page
}

export function pause(ms) {
  return new Promise((r) => setTimeout(r, ms))
}
