// Site: panel/client-updates, a new update. Shows the window of a release with the installer and its patch chosen.
// Stand: client updates on; local/files holds AmneziaGeo-1.11.0.0-win-x64-fdd.exe and .patch. Nothing is confirmed.
import { join } from "node:path"
import { fileURLToPath } from "node:url"
import { fill } from "../kit/form.mjs"

const files = join(fileURLToPath(new URL("..", import.meta.url)), "local", "files")
const version = "1.11.0.0"
const chosen = [`AmneziaGeo-${version}-win-x64-fdd.exe`, `AmneziaGeo-${version}-win-x64-fdd.patch`]

// Hands a file to the chooser of the row of the name, as the picker of the browser does.
async function choose(page, name) {
  const marked = await page.eval(`(() => {
    const row = [...document.querySelectorAll("span[title]")].find((one) => one.title === ${JSON.stringify(name)})?.parentElement
    const input = row?.querySelector("input[type=file]")
    if (!input) return false
    input.dataset.shot = "pick"
    return true
  })()`)
  if (!marked) {
    throw new Error(`no row ${name}`)
  }
  const doc = await page.send("DOM.getDocument", { depth: -1 })
  const node = await page.send("DOM.querySelector", { nodeId: doc.root.nodeId, selector: "[data-shot=pick]" })
  await page.send("DOM.setFileInputFiles", { nodeId: node.nodeId, files: [join(files, name)] })
  await page.eval(`delete document.querySelector("[data-shot=pick]").dataset.shot`)
}

export default {
  route: "/settings/client-updates/new",
  // The versions of the client releases read like addresses.
  publicAddresses: ["^1\\.1[01]\\.\\d+\\.\\d+$"],
  view: { width: 1440, height: 1500 },
  async steps({ page, settle, lang }) {
    await page.waitFor(`document.getElementById("release-version")`)
    await fill(page, { "release-version": version, "release-channel": "beta", "release-description": lang.code === "ru" ? "Сборка для проверки" : "A build for testing" })
    await settle()
    for (const name of chosen) {
      await choose(page, name)
      await settle()
    }
    await page.waitFor(`document.querySelector("[id^=release-from-]")`)
    const from = await page.eval(`document.querySelector("[id^=release-from-]").id`)
    await fill(page, { [from]: "1.10.15.0" })
    await settle()
  },
}
