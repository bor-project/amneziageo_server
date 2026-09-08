import type { Text, TextKey } from "@/i18n"

const steps: TextKey[] = ["unit.b", "unit.kb", "unit.mb", "unit.gb", "unit.tb"]

export function share(used: number, total: number): number {
  return total > 0 ? (used / total) * 100 : 0
}

export function percent(value: number): string {
  return `${value.toFixed(1)}%`
}

export function bytes(t: Text, value: number): string {
  let left = Math.max(value, 0)
  let step = 0
  while (left >= 1024 && step < steps.length - 1) {
    left /= 1024
    step++
  }

  return `${left.toFixed(step === 0 ? 0 : 2)} ${t(steps[step])}`
}

export function rate(t: Text, value: number): string {
  return t("unit.rate", { value: bytes(t, value) })
}

export function span(t: Text, seconds: number): string {
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)

  if (days > 0) {
    return `${t("unit.days", { value: days })} ${t("unit.hours", { value: hours })}`
  }

  if (hours > 0) {
    return `${t("unit.hours", { value: hours })} ${t("unit.minutes", { value: minutes })}`
  }

  if (minutes > 0) {
    return t("unit.minutes", { value: minutes })
  }

  return t("unit.seconds", { value: Math.floor(seconds) })
}

export function average(values: number[]): number {
  return values.length === 0 ? 0 : values.reduce((sum, one) => sum + one, 0) / values.length
}

export function peak(values: number[]): number {
  return values.length === 0 ? 0 : Math.max(...values)
}

export function parts(text: string): string[] {
  return text
    .split(/[,\s]+/)
    .map((one) => one.trim())
    .filter((one) => one.length > 0)
}
