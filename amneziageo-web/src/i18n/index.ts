import { useMemo } from "react"
import { en } from "./en"
import type { Dictionary, TextKey } from "./en"
import { ru } from "./ru"
import { useAppSelector } from "@/store/hooks"

export type Language = "en" | "ru"

export type LanguageChoice = "auto" | Language

export type Text = (key: TextKey, values?: Record<string, string | number>) => string

export const languageChoices: LanguageChoice[] = ["auto", "en", "ru"]

export const languageNames: Record<Language, string> = {
  en: "English",
  ru: "Русский",
}

const dictionaries: Record<Language, Dictionary> = { en, ru }

export function isLanguageChoice(value: unknown): value is LanguageChoice {
  return value === "auto" || value === "en" || value === "ru"
}

export function systemLanguage(): Language {
  const tags = navigator.languages?.length ? navigator.languages : [navigator.language]
  for (const tag of tags) {
    const head = tag.toLowerCase().split("-")[0]
    if (head === "ru" || head === "en") {
      return head
    }
  }

  return "en"
}

export function useLanguage(): Language {
  const chosen = useAppSelector((s) => s.ui.language)
  const served = useAppSelector((s) => s.ui.served)

  if (chosen !== "auto") {
    return chosen
  }

  return served === "auto" ? systemLanguage() : served
}

export function useText(): Text {
  const language = useLanguage()

  return useMemo<Text>(() => {
    const dictionary = dictionaries[language]

    return (key, values) => fill(dictionary[key], values)
  }, [language])
}

function fill(line: string, values?: Record<string, string | number>): string {
  if (!values) {
    return line
  }

  return line.replace(/\{(\w+)\}/g, (whole, name: string) => String(values[name] ?? whole))
}

export type { TextKey }
