// Site: panel/client-updates, the source Address. Shows the address and the token of the service apart and its answer.
// Stand: client updates on, source address, the service answers; the token field shows dots.
export default {
  route: "/settings/client-updates",
  // The versions of the client releases read like addresses.
  publicAddresses: ["^1\\.1[01]\\.\\d+\\.\\d+$"],
  async steps({ page, settle }) {
    await page.waitFor(`document.getElementById("client-updates-address")`)
    await settle()
  },
}
