import { useQuery } from "@tanstack/react-query"
import { client } from "./client"

export interface Portion {
  used: number
  total: number
}

export interface Processor {
  model: string
  cores: number
  threads: number
  megahertz: number
}

export interface Traffic {
  sent: number
  received: number
  upload: number
  download: number
}

export interface Sockets {
  tcp: number
  udp: number
}

export interface Tunnel {
  loaded: boolean
  version: string
  interfaces: number
}

export interface StatusWindow {
  step: number
  cpu: number[]
  memory: number[]
  swap: number[]
  storage: number[]
  upload: number[]
  download: number[]
  sockets: number[]
}

export interface Overview {
  processor: Processor
  cpu: number
  memory: Portion
  swap: Portion
  storage: Portion
  traffic: Traffic
  sockets: Sockets
  tunnel: Tunnel
  hostUptime: number
  panelUptime: number
  panelMemory: number
  panelThreads: number
  addresses: string[]
  window: StatusWindow
}

export function useOverview() {
  return useQuery({
    queryKey: ["overview"],
    queryFn: async () => (await client.get<Overview>("/overview")).data,
    refetchInterval: 2000,
  })
}
