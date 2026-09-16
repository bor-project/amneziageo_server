import { useSearchParams } from "react-router-dom"
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

export function useOrder(name = "") {
  const [params, setParams] = useSearchParams()
  const keyName = name === "" ? "sort" : `${name}Sort`
  const dirName = name === "" ? "dir" : `${name}Dir`
  const key = params.get(keyName)
  const order = key === null ? null : { key, down: params.get(dirName) === "desc" }

  function put(next: Order | null) {
    const kept = new URLSearchParams(params)

    if (next === null) {
      kept.delete(keyName)
      kept.delete(dirName)
    } else {
      kept.set(keyName, next.key)
      kept.set(dirName, next.down ? "desc" : "asc")
    }

    setParams(kept, { replace: true })
  }

  return {
    order,
    toggle: (picked: string) =>
      put(order?.key !== picked ? { key: picked, down: false } : order.down ? null : { key: picked, down: true }),
    choose: (picked: string) => put({ key: picked, down: order?.down ?? false }),
    direct: (picked: string, down: boolean) => put({ key: order?.key ?? picked, down }),
  }
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
