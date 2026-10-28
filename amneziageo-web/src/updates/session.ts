import { useSyncExternalStore } from "react"

const key = "amneziageo.updates.token"

const listeners = new Set<() => void>()

let held = read()

// The token of the service the window signed in with, null before the sign-in.
export function token(): string | null {
  return held
}

export function keepToken(next: string | null): void {
  held = next
  write(next)
  for (const listener of listeners) {
    listener()
  }
}

export function useToken(): string | null {
  return useSyncExternalStore(subscribe, token)
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)

  return () => {
    listeners.delete(listener)
  }
}

function read(): string | null {
  try {
    return sessionStorage.getItem(key)
  } catch {
    return null
  }
}

function write(value: string | null): void {
  try {
    if (value === null) {
      sessionStorage.removeItem(key)
    } else {
      sessionStorage.setItem(key, value)
    }
  } catch {
    return
  }
}
