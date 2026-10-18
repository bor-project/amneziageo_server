// Site: panel/rules, a new rule. Shows the form of a rule with the targets of the recipe "Exit through another server".
// Stand: nothing; the rule is typed into the form and not saved.
import { fill } from "../kit/form.mjs"

export default {
  route: "/routing/rules/new",
  async steps({ page }) {
    await fill(page, {
      "rule-name": "youtube",
      "rule-action": "out",
      "rule-outbound": "direct",
      "rule-protocol": "any",
      "rule-targets": "geosite:youtube\ngeoip:ag-youtube\ngeosite:netflix\nexample.com",
    })
  },
}
