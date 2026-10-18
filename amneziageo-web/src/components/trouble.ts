import type { Resolver } from "@/api/dns"

// The port the clients ask the resolver of the panel on.
export const asked = 53

// What keeps the clients from the resolver of the panel.
export type Trouble = { kind: "off" } | { kind: "port"; port: number } | { kind: "kept"; names: string[] }

// Tells what keeps the clients from the resolver of the panel, or null when nothing does or the panel has not said yet.
export function troubleOf(resolver: Resolver | undefined): Trouble | null {
  if (resolver === undefined) {
    return null
  }

  if (!resolver.isEnabled) {
    return { kind: "off" }
  }

  if (resolver.port !== asked) {
    return { kind: "port", port: resolver.port }
  }

  return resolver.state.keptOut.length > 0 ? { kind: "kept", names: resolver.state.keptOut } : null
}
