import { createContext, useContext, useEffect } from "react"

export interface Crumb {
  label: string
  to?: string
}

export interface Kept {
  head: Crumb[]
  tail: Crumb[]
  putHead: (items: Crumb[]) => void
  putTail: (items: Crumb[]) => void
}

export const Held = createContext<Kept>({ head: [], tail: [], putHead: () => undefined, putTail: () => undefined })

export function useCrumbs(items: Crumb[]) {
  const { putHead } = useContext(Held)
  useHeld(items, putHead)
}

export function useTail(items: Crumb[]) {
  const { putTail } = useContext(Held)
  useHeld(items, putTail)
}

function useHeld(items: Crumb[], put: (items: Crumb[]) => void) {
  const mark = JSON.stringify(items)

  useEffect(() => {
    put(JSON.parse(mark) as Crumb[])

    return () => put([])
  }, [mark, put])
}
