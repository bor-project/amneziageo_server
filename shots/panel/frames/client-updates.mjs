// Site: panel/client-updates. Shows the serving on with the source Own, its settings and the table of releases.
// Stand: client updates on, source own, a release in the store of the panel. The path is typed over and not saved;
// the start and the end of the token shown are replaced.
import { fill } from "../kit/form.mjs"

export default {
  route: "/settings/client-updates",
  // The versions of the client releases read like addresses.
  publicAddresses: ["^1\\.1[01]\\.\\d+\\.\\d+$"],
  view: { width: 1440, height: "fit" },
  async steps({ page, settle }) {
    await page.waitFor(`document.getElementById("client-updates-path")`)
    await fill(page, { "client-updates-path": "q7m2x9k4w1z8c3v6" })
    await settle()
  },
  mask: [{ from: "[0-9A-Za-z_-]{4}…[0-9A-Za-z_-]{2}", to: "3f9a…c1", mode: "re" }],
}
