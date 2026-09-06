import { isLanguageChoice } from "@/i18n"
import type { LanguageChoice } from "@/i18n"
import { isThemeChoice } from "@/theme/theme"
import type { ThemeChoice } from "@/theme/theme"

const themeKey = "amneziageo.theme"
const languageKey = "amneziageo.language"

export function storedTheme(): ThemeChoice {
  const value = read(themeKey)

  return isThemeChoice(value) ? value : "auto"
}

export function storedLanguage(): LanguageChoice {
  const value = read(languageKey)

  return isLanguageChoice(value) ? value : "auto"
}

export function remember(theme: ThemeChoice, language: LanguageChoice): void {
  write(themeKey, theme)
  write(languageKey, language)
}

function read(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

function write(key: string, value: string): void {
  try {
    localStorage.setItem(key, value)
  } catch {
    return
  }
}
