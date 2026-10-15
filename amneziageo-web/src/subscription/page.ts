import { copyText, draw, save, saveText } from "@/qr"
import type { Picture } from "@/qr"

interface Sheet {
  name: string
  interface: string
  endpoint: string
  fileName: string
  text: string
  link: string
}

interface View {
  title: string
  name: string
  hours: number
  upload: number
  download: number
  total: number
  configs: Sheet[]
}

type Kind = "link" | "file"

const dictionaries = {
  ru: {
    subscription: "Подписка",
    configs: { one: "{n} конфигурация", few: "{n} конфигурации", many: "{n} конфигураций", other: "{n} конфигурации" },
    hours: "Обновление раз в {n} ч",
    traffic: "Трафик за сегодня",
    today: "Сегодня ↑ {up} ↓ {down}",
    of: "из {total}",
    copyLink: "Копировать ссылку",
    saveQr: "Скачать QR",
    inQr: "Что в QR",
    link: "Ссылка vpn://",
    file: "Файл .conf",
    copy: "Копировать",
    save: "Скачать",
    copied: "Скопировано",
    notCopied: "Не удалось скопировать",
    tooBig: "Не помещается в QR-код",
    qr: "QR",
    none: "В подписке нет включённых конфигураций",
    gone: "Подписка не прочитана",
    units: ["Б", "КБ", "МБ", "ГБ", "ТБ"],
  },
  en: {
    subscription: "Subscription",
    configs: { one: "{n} configuration", few: "{n} configurations", many: "{n} configurations", other: "{n} configurations" },
    hours: "Updates every {n} h",
    traffic: "Traffic today",
    today: "Today ↑ {up} ↓ {down}",
    of: "of {total}",
    copyLink: "Copy link",
    saveQr: "Download QR",
    inQr: "What the QR holds",
    link: "vpn:// link",
    file: ".conf file",
    copy: "Copy",
    save: "Download",
    copied: "Copied",
    notCopied: "Could not copy",
    tooBig: "Does not fit into a QR code",
    qr: "QR",
    none: "The subscription holds no configuration that is on",
    gone: "The subscription was not read",
    units: ["B", "KB", "MB", "GB", "TB"],
  },
}

const language = pick()
const words = dictionaries[language]

const view = read()
const root = document.getElementById("page")!
document.documentElement.lang = language
if (view === null) {
  root.append(tag("p", "empty", words.gone))
} else {
  document.title = view.title
  root.append(head(view), block(view), cards(view))
}

function pick(): "ru" | "en" {
  for (const one of navigator.languages ?? [navigator.language]) {
    const code = one.slice(0, 2).toLowerCase()
    if (code === "ru" || code === "en") {
      return code
    }
  }

  return "en"
}

function read(): View | null {
  try {
    const data: unknown = JSON.parse(document.getElementById("feed")?.textContent ?? "null")

    return typeof data === "object" && data !== null && Array.isArray((data as View).configs) ? (data as View) : null
  } catch {
    return null
  }
}

function head(view: View): HTMLElement {
  const name = tag("span", "", `${words.subscription} `)
  name.append(tag("b", "", view.name))
  const count = new Intl.PluralRules(language).select(view.configs.length) as keyof typeof words.configs
  const meta = tag("div", "meta")
  meta.append(name, tag("span", "", fill(words.configs[count], { n: view.configs.length })), tag("span", "", fill(words.hours, { n: view.hours })))

  const usage = tag("div", "usage")
  usage.setAttribute("aria-label", words.traffic)
  const used = view.upload + view.download
  if (view.total > 0) {
    const bar = tag("div", "bar")
    const share = tag("span")
    share.style.width = `${Math.min(100, (used / view.total) * 100)}%`
    bar.append(share)
    usage.append(bar)
  }

  const numbers = tag("div", "nums")
  numbers.append(tag("span", "", fill(words.today, { up: bytes(view.upload), down: bytes(view.download) })))
  if (view.total > 0) {
    numbers.append(tag("span", "", fill(words.of, { total: bytes(view.total) })))
  }

  usage.append(numbers)
  const header = tag("header")
  header.append(tag("h1", "", view.title), meta, usage)

  return header
}

function block(view: View): HTMLElement {
  const address = location.origin + location.pathname
  const section = tag("section", "block")
  const picture = frame()
  const side = tag("div", "side")
  const toast = notice()
  const copy = button(words.copyLink, "primary")
  const keep = button(words.saveQr)
  keep.disabled = true
  copy.addEventListener("click", () => void tell(toast, copyText(address)))
  side.append(tag("h2", "", words.subscription), tag("div", "addr", address), row(copy, keep), toast)
  section.append(picture.box, side)

  void draw(address).then((drawn) => {
    picture.show(drawn)
    if (drawn !== null) {
      keep.disabled = false
      keep.addEventListener("click", () => save(`${view.name}-subscription.png`, drawn.url))
    }
  })

  return section
}

function cards(view: View): HTMLElement {
  const section = tag("section", "cards")
  section.setAttribute("aria-label", words.subscription)
  if (view.configs.length === 0) {
    section.append(tag("p", "empty", words.none))
  }

  for (const sheet of view.configs) {
    section.append(card(sheet))
  }

  return section
}

function card(sheet: Sheet): HTMLElement {
  const article = tag("article", "card")
  const top = tag("div", "top")
  top.append(tag("h3", "", sheet.name), tag("span", "chip", sheet.interface))
  article.append(top)
  if (sheet.endpoint.length > 0) {
    article.append(tag("div", "host", sheet.endpoint))
  }

  const stem = sheet.fileName.endsWith(".conf") ? sheet.fileName.slice(0, -".conf".length) : sheet.fileName
  const pictures = new Map<Kind, Promise<Picture | null>>()
  const picture = frame()
  const toast = notice()
  const copy = button(words.copy, "primary")
  const keep = button(words.save)
  const choices = tag("div", "seg")
  choices.setAttribute("role", "group")
  choices.setAttribute("aria-label", words.inQr)
  const link = button(words.link)
  const file = button(words.file)
  choices.append(link, file)
  let kind: Kind = "link"
  let shown: Picture | null = null

  function show(chosen: Kind) {
    kind = chosen
    link.setAttribute("aria-pressed", String(chosen === "link"))
    file.setAttribute("aria-pressed", String(chosen === "file"))
    if (!pictures.has(chosen)) {
      pictures.set(chosen, draw(chosen === "link" ? sheet.link : sheet.text))
    }

    void pictures.get(chosen)!.then((drawn) => {
      if (kind !== chosen) {
        return
      }

      shown = drawn
      picture.show(drawn)
      keep.disabled = chosen === "link" && drawn === null
    })
  }

  link.addEventListener("click", () => show("link"))
  file.addEventListener("click", () => show("file"))
  copy.addEventListener("click", () => void tell(toast, copyText(kind === "link" ? sheet.link : sheet.text)))
  keep.addEventListener("click", () => {
    if (kind === "file") {
      saveText(sheet.fileName, sheet.text)
    } else if (shown !== null) {
      save(`${stem}-link.png`, shown.url)
    }
  })
  article.append(choices, picture.box, row(copy, keep), toast)
  show("link")

  return article
}

// The square a QR code stands in, or the words that it does not fit.
function frame() {
  const box = tag("div", "qr")

  return {
    box,
    show(picture: Picture | null) {
      box.replaceChildren()
      box.classList.toggle("big", picture === null)
      if (picture === null) {
        box.append(words.tooBig)

        return
      }

      const image = document.createElement("img")
      image.src = picture.url
      image.alt = words.qr
      box.append(image)
    },
  }
}

function notice(): HTMLElement {
  const toast = tag("div", "toast")
  toast.setAttribute("aria-live", "polite")

  return toast
}

async function tell(toast: HTMLElement, done: Promise<boolean>) {
  const copied = await done
  toast.textContent = copied ? words.copied : words.notCopied
  toast.classList.toggle("bad", !copied)
  window.clearTimeout(Number(toast.dataset.timer ?? 0))
  toast.dataset.timer = String(window.setTimeout(() => (toast.textContent = ""), 1800))
}

function row(...buttons: HTMLElement[]): HTMLElement {
  const line = tag("div", "row")
  line.append(...buttons)

  return line
}

function button(text: string, kind = ""): HTMLButtonElement {
  const made = tag("button", kind, text)
  made.type = "button"

  return made
}

function tag<K extends keyof HTMLElementTagNameMap>(name: K, kind = "", text = ""): HTMLElementTagNameMap[K] {
  const made = document.createElement(name)
  if (kind.length > 0) {
    made.className = kind
  }

  made.textContent = text

  return made
}

function fill(text: string, values: Record<string, string | number>): string {
  return text.replace(/\{(\w+)\}/g, (whole, key: string) => (key in values ? String(values[key]) : whole))
}

function bytes(value: number): string {
  let left = Math.max(value, 0)
  let step = 0
  while (left >= 1024 && step < words.units.length - 1) {
    left /= 1024
    step++
  }

  return `${new Intl.NumberFormat(language, { maximumFractionDigits: step === 0 ? 0 : 2 }).format(left)} ${words.units[step]}`
}
