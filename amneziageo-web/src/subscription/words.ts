import type { Text } from "@/i18n"
import { en } from "@/i18n/en"
import { ru } from "@/i18n/ru"

export interface Sheet {
  name: string
  interface: string
  endpoint: string
  fileName: string
  text: string
  link: string
}

export interface View {
  title: string
  name: string
  hours: number
  upload: number
  download: number
  total: number
  configs: Sheet[]
}

export type Language = "ru" | "en"

export const words = {
  ru: {
    subscription: "Подписка",
    configs: { one: "{n} конфигурация", few: "{n} конфигурации", many: "{n} конфигураций", other: "{n} конфигурации" },
    hours: "Обновление раз в {n} ч",
    today: "Сегодня ↑ {up} ↓ {down}",
    of: "из {total}",
    none: "В подписке нет включённых конфигураций",
    gone: "Подписка не прочитана",
    open: "Открыть в AmneziaGeo",
    units: ["Б", "КБ", "МБ", "ГБ", "ТБ"],
  },
  en: {
    subscription: "Subscription",
    configs: { one: "{n} configuration", few: "{n} configurations", many: "{n} configurations", other: "{n} configurations" },
    hours: "Updates every {n} h",
    today: "Today ↑ {up} ↓ {down}",
    of: "of {total}",
    none: "The subscription holds no configuration that is on",
    gone: "The subscription was not read",
    open: "Open in AmneziaGeo",
    units: ["B", "KB", "MB", "GB", "TB"],
  },
}

// The texts of the panel in the language of the page.
export function textOf(language: Language): Text {
  const dictionary = language === "ru" ? ru : en

  return (key, values) => fill(dictionary[key], values ?? {})
}

export function fill(text: string, values: Record<string, string | number>): string {
  return text.replace(/\{(\w+)\}/g, (whole, key: string) => (key in values ? String(values[key]) : whole))
}

export function bytes(language: Language, value: number): string {
  const units = words[language].units
  let left = Math.max(value, 0)
  let step = 0
  while (left >= 1024 && step < units.length - 1) {
    left /= 1024
    step++
  }

  return `${new Intl.NumberFormat(language, { maximumFractionDigits: step === 0 ? 0 : 2 }).format(left)} ${units[step]}`
}
