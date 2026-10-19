// Site: panel/test. Shows the route test of www.youtube.com on port 443 for a client with a template, with its answer.
// Stand: a client with a template; the test reads the rules and changes nothing.
import { fill, press } from "../kit/form.mjs"

export default {
  route: "/routing/test",
  // The answer carries addresses of Google, which are public and not of the stand.
  publicAddresses: ["^142\\.25[01]\\.", "^172\\.217\\.", "^172\\.253\\.", "^216\\.58\\.", "^173\\.194\\.", "^74\\.125\\.", "^209\\.85\\.", "^64\\.233\\.", "^108\\.177\\.", "^192\\.178\\."],
  async steps({ page, t, stand, settle }) {
    await fill(page, { "test-target": "www.youtube.com", "test-port": "443", "test-protocol": "tcp", "test-client": stand.names.clientOnline })
    await press(page, t["test.run"])
    await settle()
  },
}
