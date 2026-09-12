import { useSyncExternalStore } from "react"

export const roomyQuery = "(min-width: 768px)"

export const wideQuery = "(min-width: 992px)"

export function above(query: string): boolean {
  return window.matchMedia(query).matches
}

export function useAbove(query: string): boolean {
  return useSyncExternalStore(
    (notify) => watch(query, notify),
    () => above(query),
  )
}

function watch(query: string, notify: () => void): () => void {
  const media = window.matchMedia(query)
  media.addEventListener("change", notify)
  window.addEventListener("resize", notify)

  return () => {
    media.removeEventListener("change", notify)
    window.removeEventListener("resize", notify)
  }
}
