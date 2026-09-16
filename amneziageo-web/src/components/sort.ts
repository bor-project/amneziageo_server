import { useState } from "react"
import { useLanguage } from "@/i18n"

export type SortValue = string | number | null

export interface Order {
  key: string
  down: boolean
}

export interface Place<T> {
  item: T
  at: number
}

export function useOrder() {
  const [order, setOrder] = useState<Order | null>(null)

  const toggle = (key: string) =>
    setOrder((now) => (now?.key !== key ? { key, down: false } : now.down ? null : { key, down: true }))

  return { order, toggle }
}

export function useSorted<T>(
  items: T[],
  sort: ((item: T) => SortValue) | undefined,
  order: Order | null,
): Place<T>[] {
  const language = useLanguage()
  const places = items.map((item, at) => ({ item, at }))

  if (!sort || !order) {
    return places
  }

  const collator = new Intl.Collator(language, { numeric: true, sensitivity: "base" })
  const values = items.map(sort)
  const sign = order.down ? -1 : 1

  return places.sort((a, b) => {
    const x = values[a.at]
    const y = values[b.at]

    if (blank(x) || blank(y)) {
      return blank(x) === blank(y) ? a.at - b.at : blank(x) ? 1 : -1
    }

    const diff = typeof x === "number" && typeof y === "number" ? x - y : collator.compare(String(x), String(y))

    return diff === 0 ? a.at - b.at : diff * sign
  })
}

export function ariaSort(key: string, order: Order | null) {
  if (order?.key !== key) {
    return undefined
  }

  return order.down ? "descending" : "ascending"
}

function blank(value: SortValue) {
  return value === null || value === "" || (typeof value === "number" && Number.isNaN(value))
}
