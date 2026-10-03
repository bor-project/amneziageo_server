import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export type PortState = "open" | "closed" | "unknown"

export interface FirewallPort {
  port: number
  protocol: string
  state: PortState
  engine: string
}

// Whether the panel may hold ports open in the firewall of the host, and what keeps it from doing so.
export interface FirewallReach {
  engine: string
  able: boolean
  reason: "" | "no-rights" | "host-ufw"
  message: string
}

// What a save asks of the firewall: to hold the ports open, and to leave the ports that moved open.
export interface Holding {
  opened?: boolean
  keep?: boolean
}

// What holds a port open: an endpoint, the panel or the subscriptions.
export type PortOwner = { kind: "endpoint"; id: number } | { kind: "panel" } | { kind: "subscriptions" }

export function usePortState(port: number, enabled: boolean, protocol: "tcp" | "udp" = "tcp") {
  return useQuery({
    queryKey: ["firewall", "port", protocol, port],
    queryFn: async () => (await client.get<FirewallPort>("/firewall/port", { params: { port, protocol } })).data,
    enabled: enabled && Number.isInteger(port) && port > 0 && port <= 65535,
    staleTime: 10000,
  })
}

// Asks once a while whether the panel may change the firewall of the host.
export function useFirewall(enabled = true) {
  return useQuery({
    queryKey: ["firewall", "reach"],
    queryFn: async () => (await client.get<FirewallReach>("/firewall")).data,
    enabled,
    staleTime: 300000,
    retry: false,
  })
}

// Holds the ports of an endpoint, the panel or the subscriptions open at once.
export function useOpenPort() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (owner: PortOwner) => {
      await client.post(pathOf(owner))
    },
    onSettled: async (_answer, _error, owner) => {
      await queryClient.invalidateQueries({ queryKey: ["firewall", "port"] })
      await queryClient.invalidateQueries({ queryKey: [keyOf(owner)] })
    },
  })
}

export function outside(listen: string[]) {
  return listen.length === 0 || listen.some((address) => !loopback(address))
}

function loopback(address: string) {
  return address === "::1" || address === "localhost" || address.startsWith("127.")
}

function pathOf(owner: PortOwner): string {
  if (owner.kind === "endpoint") {
    return `/configs/${owner.id}/open`
  }

  return owner.kind === "panel" ? "/panel/open" : "/subscription/open"
}

function keyOf(owner: PortOwner): string {
  if (owner.kind === "endpoint") {
    return "configs"
  }

  return owner.kind === "panel" ? "panel" : "subscription"
}
