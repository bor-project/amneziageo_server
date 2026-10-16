import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import "@/index.css"
import { SubscriptionPage } from "@/subscription/SubscriptionPage"
import type { Language, View } from "@/subscription/words"

const language = pick()
const dark = window.matchMedia("(prefers-color-scheme: dark)")
const view = read()

document.documentElement.lang = language
paint()
dark.addEventListener("change", paint)
if (view !== null) {
  document.title = view.title
}

createRoot(document.getElementById("page")!).render(
  <StrictMode>
    <SubscriptionPage view={view} language={language} />
  </StrictMode>,
)

function pick(): Language {
  for (const one of navigator.languages ?? [navigator.language]) {
    const code = one.slice(0, 2).toLowerCase()
    if (code === "ru" || code === "en") {
      return code
    }
  }

  return "en"
}

function paint() {
  document.documentElement.classList.toggle("dark", dark.matches)
}

function read(): View | null {
  try {
    const data: unknown = JSON.parse(document.getElementById("feed")?.textContent ?? "null")

    return typeof data === "object" && data !== null && Array.isArray((data as View).configs) ? (data as View) : null
  } catch {
    return null
  }
}
