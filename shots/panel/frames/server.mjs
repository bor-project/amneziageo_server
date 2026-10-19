// Site: panel/server. Shows the Server tab of the settings, the language of the panel set to the language of the shot.
// Stand: a panel with a domain.
import { set } from "../kit/form.mjs"

export default {
  route: "/settings/server",
  async steps({ page, lang }) {
    await set(page, "panel-language", lang.code)
  },
}
