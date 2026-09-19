import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"
import type { KeyPair, Obfuscation } from "./configs"

export type OutboundKind = "local" | "wg" | "ws"

export interface OutboundProbe {
  isReached: boolean
  falls: number
  at: string
}

export interface OutboundState {
  hasLink: boolean
  endpoint: string
  lastHandshake: string | null
  rxBytes: number | null
  txBytes: number | null
  isAlive: boolean
  fault: string
  carries: boolean
  probe: OutboundProbe | null
}

export interface Outbound {
  id: number
  name: string
  kind: OutboundKind
  position: number
  isEnabled: boolean
  host: string
  port: number
  proxy: string
  publicKey: string
  peerKey: string
  privateKey: string | null
  presharedKey: string | null
  address: string[]
  dns: string[]
  mtu: number
  keepalive: number
  probe: string
  probeEvery: number
  closePrivate: boolean
  mark: number
  table: number
  obfuscation: Obfuscation
  state: OutboundState | null
  createdUtc: string
  updatedUtc: string
}

export interface OutboundDraft {
  name: string
  kind: OutboundKind
  isEnabled: boolean
  host: string
  port: number
  proxy: string
  privateKey: string
  peerKey: string
  presharedKey: string
  address: string[]
  dns: string[]
  mtu: number
  keepalive: number
  probe: string
  probeEvery: number
  closePrivate: boolean
  obfuscation: Obfuscation
}

export function useOutbounds() {
  return useQuery({
    queryKey: ["outbounds"],
    queryFn: async () => (await client.get<Outbound[]>("/outbounds")).data,
  })
}

export function useFreshOutbound(enabled: boolean, name: string, kind: OutboundKind) {
  return useQuery({
    queryKey: ["outbounds", "draft", name, kind],
    queryFn: async () =>
      (await client.get<Outbound>(`/outbounds/draft?name=${encodeURIComponent(name)}&kind=${kind}`)).data,
    enabled,
    gcTime: 0,
    staleTime: 0,
  })
}

export function useAddOutbound() {
  return useRefreshing((draft: OutboundDraft) => client.post("/outbounds", draft))
}

export function useChangeOutbound() {
  return useRefreshing(({ id, draft }: { id: number; draft: OutboundDraft }) => client.put(`/outbounds/${id}`, draft))
}

export function useRemoveOutbound() {
  return useRefreshing((id: number) => client.delete(`/outbounds/${id}`))
}

export function useMoveOutbound() {
  return useRefreshing(({ id, up }: { id: number; up: boolean }) => client.post(`/outbounds/${id}/move`, { up }))
}

export function useSwitchOutbound() {
  return useRefreshing(({ id, on }: { id: number; on: boolean }) => client.post(`/outbounds/${id}/switch`, { on }))
}

export function useApplyOutbound() {
  return useRefreshing((id: number) => client.post(`/outbounds/${id}/apply`))
}

export function useProbeOutbound() {
  return useRefreshing((id: number) => client.post(`/outbounds/${id}/probe`))
}

export function useImportOutbound() {
  return useMutation({
    mutationFn: async ({ name, config }: { name: string; config: string }) =>
      (await client.post<Outbound>("/outbounds/import", { name, config })).data,
  })
}

export function useOutboundKeys() {
  return useMutation({
    mutationFn: async () => (await client.post<KeyPair>("/outbounds/keys")).data,
  })
}

export function draftOf(outbound: Outbound): OutboundDraft {
  return {
    name: outbound.name,
    kind: outbound.kind,
    isEnabled: outbound.isEnabled,
    host: outbound.host,
    port: outbound.port,
    proxy: outbound.proxy,
    privateKey: outbound.privateKey ?? "",
    peerKey: outbound.peerKey,
    presharedKey: outbound.presharedKey ?? "",
    address: outbound.address,
    dns: outbound.dns,
    mtu: outbound.mtu,
    keepalive: outbound.keepalive,
    probe: outbound.probe,
    probeEvery: outbound.probeEvery,
    closePrivate: outbound.closePrivate,
    obfuscation: outbound.obfuscation,
  }
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["outbounds"] })
    },
  })
}
