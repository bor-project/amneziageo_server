// Replaces the names and addresses of the stand on the page and keeps them replaced while the page redraws.
(rules) => {
  const escape = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
  const compiled = rules
    .slice()
    .sort((a, b) => b.from.length - a.from.length)
    .map((r) => {
      if (r.mode === "re") {
        return [new RegExp(r.from, "g"), r.to]
      }
      if (r.mode === "word") {
        return [new RegExp(`(?<![\\w/])${escape(r.from)}(?![\\w/])`, "g"), r.to]
      }
      return [new RegExp(escape(r.from), "gi"), r.to]
    })
  const fakeKey = (key) => {
    let h = 2166136261
    for (const c of key) {
      h = Math.imul(h ^ c.charCodeAt(0), 16777619) >>> 0
    }
    const bytes = new Uint8Array(32)
    for (let i = 0; i < 32; i++) {
      h = Math.imul(h ^ (h >>> 15), 2246822507) >>> 0
      bytes[i] = h & 255
    }
    return btoa(String.fromCharCode(...bytes))
  }
  const fix = (s) => {
    let r = s
    for (const [re, to] of compiled) {
      r = r.replace(re, to)
    }
    return r.replace(/(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=/g, fakeKey)
  }
  const apply = (root) => {
    const walk = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
    while (walk.nextNode()) {
      const n = walk.currentNode
      const v = fix(n.nodeValue)
      if (v !== n.nodeValue) {
        n.nodeValue = v
      }
    }
    for (const el of root.querySelectorAll ? root.querySelectorAll("input, textarea") : []) {
      const v = fix(el.value)
      if (v !== el.value) {
        el.value = v
      }
    }
    for (const el of root.querySelectorAll ? root.querySelectorAll("[title], [placeholder], [aria-label]") : []) {
      for (const a of ["title", "placeholder", "aria-label"]) {
        const old = el.getAttribute(a)
        if (old !== null && fix(old) !== old) {
          el.setAttribute(a, fix(old))
        }
      }
    }
  }
  window.__agMaskObserver?.disconnect()
  apply(document.body)
  const watch = { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ["title", "placeholder", "value"] }
  const observer = new MutationObserver(() => {
    observer.disconnect()
    apply(document.body)
    observer.observe(document.body, watch)
  })
  observer.observe(document.body, watch)
  window.__agMaskObserver = observer
  document.title = fix(document.title)
}
