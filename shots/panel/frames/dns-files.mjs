// Site: panel/dns, DNS in client configurations. Shows the preferred DNS of the server and what the files of each
// interface name now, the part alone.
// Stand: the resolver on, two interfaces.
const part = (t) =>
  `[...document.querySelectorAll("main div")].find((one) => one.childElementCount === 0 && one.textContent.trim() === ${JSON.stringify(t["dns.files"])})?.parentElement`

export default {
  route: "/routing/dns",
  async steps({ page, t, settle }) {
    if (!(await page.eval(`!!(${part(t)})`))) {
      throw new Error(`no part ${t["dns.files"]} on the page`)
    }
    await page.eval(`${part(t)}.scrollIntoView({ block: "center" })`)
    await settle()
  },
  clip: ({ page, t }) =>
    page.eval(`(() => {
      const box = ${part(t)}.getBoundingClientRect()
      return { x: box.left - 12, y: box.top + window.scrollY - 12, width: box.width + 24, height: box.height + 24 }
    })()`),
}
