import { useEffect, useSyncExternalStore } from "react"
import { useLanguage } from "@/i18n"
import { useAppSelector } from "@/store/hooks"

export type Theme = "light" | "dark"

export type ThemeChoice = "auto" | Theme

export const themeChoices: ThemeChoice[] = ["auto", "light", "dark"]

const darkQuery = "(prefers-color-scheme: dark)"

export function isThemeChoice(value: unknown): value is ThemeChoice {
  return value === "auto" || value === "light" || value === "dark"
}

export function systemTheme(): Theme {
  return window.matchMedia(darkQuery).matches ? "dark" : "light"
}

export function useTheme(): Theme {
  const chosen = useAppSelector((s) => s.ui.theme)
  const system = useSyncExternalStore(watchTheme, systemTheme)

  return chosen === "auto" ? system : chosen
}

export function useAppearance(): void {
  const theme = useTheme()
  const language = useLanguage()

  useEffect(() => {
    document.documentElement.classList.toggle("dark", theme === "dark")
    document.documentElement.lang = language
  }, [theme, language])
}

function watchTheme(notify: () => void): () => void {
  const media = window.matchMedia(darkQuery)
  media.addEventListener("change", notify)

  return () => media.removeEventListener("change", notify)
}
