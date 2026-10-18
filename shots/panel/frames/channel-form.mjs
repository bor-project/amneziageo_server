// Site: panel/channels, a new channel. Shows the form of an AmneziaWG channel to another server.
// Stand: nothing; the channel is typed into the form and not saved.
import { fill } from "../kit/form.mjs"

export default {
  route: "/routing/channels/new",
  async steps({ page }) {
    await fill(page, {
      "outbound-name": "finland",
      "outbound-kind": "wg",
      "outbound-host": "198.51.100.20",
      "outbound-port": "51820",
      "outbound-address": "10.66.0.2/32",
    })
  },
}
