// Site: panel/basic. Shows the block and direct lists with the uses the page names, the form not saved.
// Stand: nothing; the lists are typed into the form and not saved.
import { fill } from "../kit/form.mjs"

export default {
  route: "/routing/basic",
  async steps({ page }) {
    await fill(page, {
      "basic-block": "geosite:category-ads-all\nkeyword:ads",
      "basic-direct": "192.168.1.0/24\nexample.com",
    })
  },
}
