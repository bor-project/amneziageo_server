// Shoots the frames of the panel for the site: node shots/panel/shoot.mjs <language|all> [frame ...]
import { existsSync, mkdirSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath, pathToFileURL } from "node:url"
import { connect, pause } from "./kit/cdp.mjs"

const here = dirname(fileURLToPath(import.meta.url))
const repo = join(here, "..", "..")
const stand = JSON.parse(readFileSync(join(here, "local", "stand.json"), "utf8"))
// The window of the client updates server apart, for the jobs with app "updates".
const updates = stand.updates ? { url: stand.updates.url, token: readFileSync(stand.updates.tokenFile, "utf8").trim() } : null
const rules = JSON.parse(readFileSync(join(here, "local", "mask.json"), "utf8"))
const languages = JSON.parse(readFileSync(join(here, "languages.json"), "utf8"))
const maskSource = readFileSync(join(here, "kit", "mask.js"), "utf8")
const order = readFileSync(join(here, "frames.txt"), "utf8")
  .split("\n")
  .map((l) => l.trim())
  .filter((l) => l && !l.startsWith("#"))
  .map((l) => l.split(/\s+/)[0])

const [which = "all", ...picked] = process.argv.slice(2)
const langs = which === "all" ? languages.map((l) => l.code) : [which]
const names = picked.length ? picked : order
const view = { width: 1440, height: 900, scale: 1 }

const quiet = `(() => {
  if (!document.getElementById("ag-shot-style")) {
    const s = document.createElement("style")
    s.id = "ag-shot-style"
    s.textContent = "*, *::before, *::after { transition: none !important; animation: none !important; caret-color: transparent !important }"
    document.head.append(s)
  }
})()`

// What a shot must not show once the masks are on: names of dynamic DNS, public addresses and the masked strings.
const leaks = (forbidden, allow = []) => `(() => {
  const texts = []
  const walk = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT)
  while (walk.nextNode()) { if (walk.currentNode.nodeValue.trim() && walk.currentNode.parentElement?.offsetParent !== null) texts.push(walk.currentNode.nodeValue) }
  for (const i of document.querySelectorAll("input, textarea")) texts.push(i.value)
  for (const s of document.querySelectorAll("select")) texts.push(...[...s.selectedOptions].map((o) => o.text))
  for (const e of document.querySelectorAll("[title], [placeholder], [aria-label]")) texts.push(e.getAttribute("title") ?? "", e.getAttribute("placeholder") ?? "", e.getAttribute("aria-label") ?? "")
  texts.push(document.title)
  const forbidden = ${JSON.stringify(forbidden)}.map((r) => new RegExp(r.source, r.flags))
  const keys = window.__agKeys ?? []
  const public_ = ${JSON.stringify(allow)}.map((a) => new RegExp(a))
  const found = new Set()
  const priv = (a, b) => a === 10 || a === 127 || a === 0 || (a === 172 && b >= 16 && b < 32) || (a === 192 && b === 168) ||
    (a === 100 && b >= 64 && b < 128) || (a === 169 && b === 254) || a >= 224 || (a === 198 && (b === 18 || b === 19))
  const allowed = /^(192\\.0\\.2|198\\.51\\.100|203\\.0\\.113)\\.|^(1\\.1\\.1\\.1|1\\.0\\.0\\.1|8\\.8\\.8\\.8|8\\.8\\.4\\.4|9\\.9\\.9\\.9|149\\.112\\.112\\.112|77\\.88\\.8\\.[18])$/
  for (const t of texts) {
    for (const m of t.matchAll(/\\b(?:[a-z0-9-]+\\.)+(?:ddns\\.net|sytes\\.net|duckdns\\.org|hopto\\.org|zapto\\.org)\\b/gi)) found.add("dynamic name " + m[0].split(".").slice(-2).join("."))
    for (const m of t.matchAll(/\\b(\\d{1,3})\\.(\\d{1,3})\\.(\\d{1,3})\\.(\\d{1,3})\\b(?![\\/\\d])/g)) {
      if (!priv(+m[1], +m[2]) && !allowed.test(m[0]) && !public_.some((a) => a.test(m[0]))) found.add("address " + m[1] + "." + m[2] + ".x.x")
    }
    for (let i = 0; i < forbidden.length; i++) {
      if (forbidden[i].test(t)) found.add("mask rule " + i)
    }
    for (const k of keys) {
      if (t.includes(k)) found.add("a key of the stand")
    }
  }
  return [...found]
})()`

const dictionary = (code) => {
  const file = join(repo, "amneziageo-web", "src", "i18n", `${code}.ts`)
  const out = {}
  for (const m of readFileSync(file, "utf8").matchAll(/^\s*"([^"]+)":\s*"((?:[^"\\]|\\.)*)",?\s*$/gm)) {
    out[m[1]] = JSON.parse(`"${m[2]}"`)
  }
  return out
}

async function settle(page) {
  await page.waitFor(`document.readyState === "complete" && document.querySelector("nav, form, main")`, 20000)
  let last = ""
  let same = 0
  for (let i = 0; i < 40 && same < 3; i++) {
    await pause(300)
    const now = await page.eval(`document.body.innerText.replace(/[\\d.,:]+/g, "")`)
    same = now === last ? same + 1 : 0
    last = now
  }
}

// The tab of the panel, or of the window of the updates server where an earlier shot left it.
const page = await connect(stand.port, stand.url).catch((failure) => (updates ? connect(stand.port, updates.url) : Promise.reject(failure)))
await page.viewport(view.width, view.height, view.scale)
const escape = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
const forbidden = rules
  .filter((r) => r.mode !== "re" && r.check !== false && !r.frames)
  .map((r) => (r.mode === "word" ? { source: `(?<![\\w/])${escape(r.from)}(?![\\w/])`, flags: "" } : { source: escape(r.from), flags: "i" }))
const keepKeys = `(() => {
  const texts = [...document.querySelectorAll("input, textarea")].map((i) => i.value)
  const walk = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT)
  while (walk.nextNode()) texts.push(walk.currentNode.nodeValue)
  window.__agKeys = [...new Set(texts.flatMap((t) => t.match(/[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=/g) ?? []))]
  return window.__agKeys.length
})()`
let failed = 0
for (const code of langs) {
  const lang = languages.find((l) => l.code === code)
  if (!lang) {
    throw new Error(`no language ${code} in languages.json`)
  }
  const t = dictionary(code)
  const out = join(here, "out", code)
  mkdirSync(out, { recursive: true })
  for (const name of names) {
    const file = join(here, "frames", `${name}.mjs`)
    if (!existsSync(file)) {
      console.log(`${code} ${name}: no job frames/${name}.mjs`)
      failed++
      continue
    }
    const frame = (await import(pathToFileURL(file).href)).default
    const apart = frame.app === "updates"
    const site = apart ? updates?.url : stand.url
    if (!site) {
      console.log(`${code} ${name}: local/stand.json names no ${apart ? "updates server" : "panel"}`)
      failed++
      continue
    }
    const size = frame.view ?? view
    // A view of the height "fit" is as tall as the page: a bar of a form stays where the page puts it.
    await page.viewport(size.width, size.height === "fit" ? 400 : size.height, size.scale ?? view.scale)
    if (new URL(await page.eval("location.href")).origin !== new URL(site).origin) {
      await page.send("Page.navigate", { url: site })
      await pause(1500)
    }
    await page.eval(`localStorage.setItem("amneziageo.language", ${JSON.stringify(code)}); localStorage.setItem("amneziageo.theme", "light")`)
    const route = typeof frame.route === "function" ? frame.route(stand) : frame.route
    await page.send("Page.navigate", { url: new URL(route, site).href })
    await pause(500)
    await settle(page)
    await page.eval(quiet)
    const mark = apart ? t["updates.title"] : t["nav.overview"]
    if (await page.eval(`!document.body.innerText.includes(${JSON.stringify(mark)})`)) {
      console.log(`${code} ${name}: the ${apart ? "window" : "panel"} is not in ${code} or not signed in`)
      failed++
      continue
    }
    const ctx = { page, t, pause, settle: () => settle(page), stand, lang, updates }
    if (frame.steps) {
      await frame.steps(ctx)
    }
    if (size.height === "fit") {
      await page.viewport(size.width, await page.eval("document.documentElement.scrollHeight"), size.scale ?? view.scale)
      await settle(page)
    }
    const keys = await page.eval(keepKeys)
    const own = rules.filter((r) => r.frames?.includes(name))
    const shared = rules.filter((r) => !r.frames)
    await page.eval(`(${maskSource})(${JSON.stringify([...own, ...(frame.mask ?? []), ...shared])})`)
    if (frame.after) {
      await frame.after(ctx)
    }
    await pause(400)
    const found = await page.eval(leaks(forbidden, frame.publicAddresses))
    if (found.length) {
      console.log(`${code} ${name}: not saved, the page still shows ${found.join(", ")}`)
      failed++
      continue
    }
    const clip = frame.clip ? await frame.clip(ctx) : undefined
    await page.shot(join(out, `${name}.png`), clip)
    console.log(`${code} ${name}: out/${code}/${name}.png${keys ? `, keys replaced ${keys}` : ""}`)
  }
}
page.close()
process.exitCode = failed ? 1 : 0
