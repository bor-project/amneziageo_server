// Site: recipes/updates-service, the window of the server. Shows the window of the client updates server apart signed
// in: the address of the releases for panels and apps, the directory with its room, the releases.
// Stand: the client updates server with its window and releases, named by updates in local/stand.json. The window opened
// by an address of the stand shows it as the address of the releases; the frame shows the address of the recipe.
import { fill, press } from "../kit/form.mjs"

export default {
  app: "updates",
  route: "./",
  // The versions of the client releases read like addresses.
  publicAddresses: ["^1\\.1[01]\\.\\d+\\.\\d+$"],
  view: { width: 1440, height: "fit" },
  async steps({ page, t, settle, updates }) {
    if (await page.eval(`!!document.getElementById("token")`)) {
      await fill(page, { token: updates.token })
      await press(page, t["login.submit"])
    }
    await page.waitFor(`document.getElementById("updates-address")`)
    await settle()
  },
  mask: [{ from: "^https://[^/]+/$", to: "https://updates.example.com:8445/", mode: "re" }],
}
