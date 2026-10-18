// Site: panel/clients, the Export tab. Shows the three QR cards of a configuration with their buttons.
// Stand: a client without a template and with a subscription address; the QR codes are replaced by invented ones.
import { swapQr } from "../kit/fake.mjs"

export default {
  route: (s) => `/clients/${s.ids.clientPlain}/export`,
  async after({ page, t }) {
    const kinds = await swapQr(page, t)
    if (kinds.length < 2) {
      throw new Error(`QR cards on the page: ${kinds.join(", ")}`)
    }
  },
}
