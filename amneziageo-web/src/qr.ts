export interface Picture {
  url: string
  modules: number
}

export async function draw(text: string): Promise<Picture | null> {
  const qr = (await import("qrcode")).default
  const fit = (level: "M" | "L") => {
    try {
      return qr.create(text, { errorCorrectionLevel: level }).modules.size + 2
    } catch {
      return 0
    }
  }
  const strong = fit("M")
  const level = strong > 0 ? "M" : "L"
  const modules = strong > 0 ? strong : fit("L")
  if (modules === 0) {
    return null
  }

  return { url: await qr.toDataURL(text, { errorCorrectionLevel: level, margin: 1, scale: 8 }), modules }
}

// A page served over plain HTTP gets no clipboard from the browser, so the text goes through a selection then.
export async function copyText(text: string): Promise<boolean> {
  if (navigator.clipboard !== undefined) {
    try {
      await navigator.clipboard.writeText(text)

      return true
    } catch {
      return select(text)
    }
  }

  return select(text)
}

function select(text: string): boolean {
  const before = document.activeElement instanceof HTMLElement ? document.activeElement : null
  const area = document.createElement("textarea")
  area.value = text
  area.readOnly = true
  area.style.position = "fixed"
  area.style.top = "0"
  area.style.left = "-9999px"
  document.body.append(area)
  area.select()
  area.setSelectionRange(0, text.length)

  try {
    return document.execCommand("copy")
  } catch {
    return false
  } finally {
    area.remove()
    before?.focus()
  }
}

export async function copyImage(picture: Picture | null): Promise<boolean> {
  if (picture === null) {
    return false
  }

  try {
    const blob = blobOf(picture.url)
    await navigator.clipboard.write([new ClipboardItem({ [blob.type]: blob })])

    return true
  } catch {
    return false
  }
}

// Turns a data URL into its file without a request, which the policy of the page of a subscription forbids.
function blobOf(url: string): Blob {
  const comma = url.indexOf(",")
  const type = url.slice("data:".length, comma).split(";")[0]
  const bytes = Uint8Array.from(atob(url.slice(comma + 1)), (one) => one.charCodeAt(0))

  return new Blob([bytes], { type })
}

export function save(name: string, url: string) {
  const link = document.createElement("a")
  link.href = url
  link.download = name
  link.click()
}

export function saveText(name: string, text: string) {
  const url = URL.createObjectURL(new Blob([text], { type: "text/plain" }))
  save(name, url)
  URL.revokeObjectURL(url)
}
