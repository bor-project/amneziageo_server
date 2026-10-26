// Site: panel/presets, a new preset. Shows the form of a new routing preset with "Everything through VPN" on and the
// warning it brings.
// Stand: nothing; the flag is set in the form and not saved.
import { fill } from "../kit/form.mjs"

export default {
  route: "/clients/presets/new",
  view: { width: 1440, height: 1060 },
  async steps({ page }) {
    await fill(page, { "preset-full": true })
  },
}
