const storageKey = "amneziageo.refresh"

let accessToken: string | null = null

export interface Tokens {
  access: string
  refresh: string | null
}

export function access(): string | null {
  return accessToken
}

export function refresh(): string | null {
  try {
    return localStorage.getItem(storageKey)
  } catch {
    return null
  }
}

export function keep(tokens: Tokens): void {
  accessToken = tokens.access
  try {
    if (tokens.refresh) {
      localStorage.setItem(storageKey, tokens.refresh)
    } else {
      localStorage.removeItem(storageKey)
    }
  } catch {
    return
  }
}

export function drop(): void {
  accessToken = null
  try {
    localStorage.removeItem(storageKey)
  } catch {
    return
  }
}
