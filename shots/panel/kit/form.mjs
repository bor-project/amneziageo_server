// Fills the controls of a form by their ids the way typing does, without saving the form.
export async function set(page, id, value) {
  const done = await page.eval(`(() => {
    const el = document.getElementById(${JSON.stringify(id)})
    if (!el) return false
    if (el.type === "checkbox") {
      if (el.checked !== ${JSON.stringify(value)}) el.click()
      return true
    }
    const proto = el.tagName === "TEXTAREA" ? HTMLTextAreaElement : el.tagName === "SELECT" ? HTMLSelectElement : HTMLInputElement
    Object.getOwnPropertyDescriptor(proto.prototype, "value").set.call(el, ${JSON.stringify(value)})
    el.dispatchEvent(new Event("input", { bubbles: true }))
    el.dispatchEvent(new Event("change", { bubbles: true }))
    return true
  })()`)
  if (!done) {
    throw new Error(`no control #${id}`)
  }
}

export async function fill(page, values) {
  for (const [id, value] of Object.entries(values)) {
    await set(page, id, value)
  }
}

// Presses the button whose text is the given string of the panel.
export async function press(page, text) {
  const done = await page.eval(`(() => {
    const b = [...document.querySelectorAll("button")].find((b) => b.innerText.trim() === ${JSON.stringify(text)})
    if (!b) return false
    b.click()
    return true
  })()`)
  if (!done) {
    throw new Error(`no button "${text}"`)
  }
}
