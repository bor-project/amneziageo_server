import { useQuery } from "@tanstack/react-query"
import { client } from "./client"

export type PortState = "open" | "closed" | "unknown"

export interface FirewallPort {
  port: number
  protocol: string
  state: PortState
  engine: string
}

export function usePortState(port: number, enabled: boolean) {
  return useQuery({
    queryKey: ["firewall", "port", port],
    queryFn: async () => (await client.get<FirewallPort>("/firewall/port", { params: { port } })).data,
    enabled: enabled && Number.isInteger(port) && port > 0 && port <= 65535,
    staleTime: 10000,
  })
}

export function outside(listen: string[]) {
  return listen.length === 0 || listen.some((address) => !loopback(address))
}

function loopback(address: string) {
  return address === "::1" || address === "localhost" || address.startsWith("127.")
}
