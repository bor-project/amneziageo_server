import { useAppSelector } from "./hooks"

const key = "amneziageo.spots"

export type Spots = Record<string, string>

export function sectionOf(pathname: string): string {
  return pathname.split("/")[1] ?? ""
}

export function spotIn(spots: Spots, path: string): string {
  return path + (spots[path] ?? "")
}

export function useSpots(): Spots {
  return useAppSelector((s) => s.spots.query)
}

export function useSpot(path: string): string {
  return spotIn(useSpots(), path)
}

export function storedSpots(): Spots {
  const kept = read(key)

  return kept === null ? {} : parsed(kept)
}

export function rememberSpots(spots: Spots): void {
  write(key, JSON.stringify(spots))
}

function parsed(value: string): Spots {
  const spots: Spots = {}

  try {
    const kept: unknown = JSON.parse(value)

    if (kept !== null && typeof kept === "object") {
      for (const [path, search] of Object.entries(kept)) {
        if (typeof search === "string") {
          spots[path] = search
        }
      }
    }
  } catch {
    return spots
  }

  return spots
}

function read(name: string): string | null {
  try {
    return sessionStorage.getItem(name)
  } catch {
    return null
  }
}

function write(name: string, value: string): void {
  try {
    sessionStorage.setItem(name, value)
  } catch {
    return
  }
}
