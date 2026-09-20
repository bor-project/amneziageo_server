const prefix = "amneziageo.spot."

export function sectionOf(pathname: string): string {
  return pathname.split("/")[1] ?? ""
}

export function keepSpot(pathname: string, search: string): void {
  write(prefix + sectionOf(pathname), pathname + search)
}

export function lastSpot(section: string, fallback: string): string {
  const kept = read(prefix + section)

  return kept === null || kept === "" ? fallback : kept
}

function read(key: string): string | null {
  try {
    return sessionStorage.getItem(key)
  } catch {
    return null
  }
}

function write(key: string, value: string): void {
  try {
    sessionStorage.setItem(key, value)
  } catch {
    return
  }
}
