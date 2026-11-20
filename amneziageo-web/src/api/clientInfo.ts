import { useQuery } from "@tanstack/react-query"
import { client } from "./client"
import type { AccessSummary } from "./access"
import type { TrafficLimits, TrafficLimitsState } from "./trafficLimits"

export interface ClientInformation {
  id: number
  name: string
  isEnabled: boolean
  createdUtc: string
  updatedUtc: string
  connection: {
    isOnline: boolean
    isPresent: boolean
    lastHandshake: string | null
    lastActivity: string | null
    sessionStartedUtc: string | null
    endpoint: string | null
    cut: string[]
    reason: string | null
  }
  traffic: {
    peerRxBytes: number | null
    peerTxBytes: number | null
    rxRate: number | null
    txRate: number | null
    todayRx: number | null
    todayTx: number | null
    day: string
    timeZone: string
    history: { day: string; rxBytes: number; txBytes: number }[]
  }
  limits: {
    status: TrafficLimitsState | null
    policy: TrafficLimits | null
    source: "client" | "template" | "none"
    templateId: number | null
    legacyDailyLimit: number
  }
  configuration: {
    id: number
    name: string
    isEnabled: boolean
    address: string[]
    templateId: number | null
    templateName: string | null
    endpoint: string
    dns: string[]
    dnsSource: "client" | "template" | "server" | "stand-ins"
    dnsMode: string
    allowedIps: string[]
    allowedIpsTotal: number
    allowedIpsTruncated: boolean
    allowedIpsSource: "template" | "interface" | "default"
    mtu: number
    mtuSource: "template" | "interface"
    keepalive: number
    keepaliveSource: "template" | "interface"
    inbound: string
    presets: { id: number; name: string }[]
    presetsOnly: boolean
    proxyIds: number[]
    requireUpdate: boolean
  } | null
  statistics: {
    canRead: boolean
    isEnabled: boolean | null
    isRunning: boolean | null
    fault: string | null
  }
}

export interface ClientStatistics {
  bindingSince: string
  from: string
  to: string
  isEnabled: boolean
  isRunning: boolean
  fault: string | null
  summary: AccessSummary
}

export function useClientInfo(id: number, days: number) {
  return useQuery({
    queryKey: ["client-info", id, days],
    queryFn: async () => (await client.get<ClientInformation>(`/clients/${id}/info`, { params: { days } })).data,
    refetchInterval: 2000,
  })
}

export function useClientStatistics(id: number, hours: number, enabled: boolean, range?: { from: string; to: string }) {
  return useQuery({
    queryKey: ["client-statistics", id, hours, range?.from, range?.to],
    queryFn: async () => {
      const to = new Date()
      const from = new Date(to.getTime() - hours * 3600000)

      return (await client.get<ClientStatistics>(`/clients/${id}/statistics`, {
        params: range ?? { from: from.toISOString(), to: to.toISOString() }, timeout: 60000,
      })).data
    },
    refetchInterval: 15000,
    enabled,
  })
}
