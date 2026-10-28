// Site: recipes/updates-service, the window of the server. Shows the sign-in to the window of the client updates server
// apart: the token, the version, the language and the theme, the card alone.
// Stand: the client updates server with its window, named by updates in local/stand.json. The window is signed out first.
export default {
  app: "updates",
  route: "login",
  // The version of the server reads like an address.
  publicAddresses: ["^\\d+\\.\\d+\\.\\d+\\.\\d+$"],
  async steps({ page, settle }) {
    if (await page.eval(`sessionStorage.getItem("amneziageo.updates.token") !== null`)) {
      await page.eval(`sessionStorage.removeItem("amneziageo.updates.token"); location.replace(new URL("login", document.baseURI))`)
      await settle()
    }
    await page.waitFor(`document.getElementById("token")`)
    await settle()
  },
  clip: ({ page }) =>
    page.eval(`(() => {
      const box = document.querySelector("form").getBoundingClientRect()
      return { x: box.left - 40, y: box.top - 40, width: box.width + 80, height: box.height + 80 }
    })()`),
}
