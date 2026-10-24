// Site: panel/templates, the form. Shows the entries of a template, route by name on and the routing presets with the
// sign of the AmneziaGeo app.
// Stand: a template with geosite keys and route by name.
const caption = (t) =>
  `[...document.querySelectorAll("main *")].find((one) => [...one.childNodes].some((node) => node.nodeType === 3 && node.textContent.trim() === ${JSON.stringify(t["templates.presets"])}))`

export default {
  route: (s) => `/clients/templates/${s.ids.template}/edit`,
  async steps({ page, t, settle }) {
    if (!(await page.eval(`!!(${caption(t)})`))) {
      throw new Error(`no field ${t["templates.presets"]} on the page`)
    }
    await page.eval(`${caption(t)}.scrollIntoView({ block: "center" }); document.scrollingElement.scrollBy(0, -90)`)
    await settle()
  },
}
