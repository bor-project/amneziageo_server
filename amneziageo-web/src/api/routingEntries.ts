import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export type RuleAction = "out" | "direct" | "block"
export type RuleProtocol = "any" | "tcp" | "udp"

export interface RuleState {
  isLive: boolean
  isHeld: boolean
  fault: string
  message: string
  mark: number
  ranges: number
  names: number
}

export interface EntryCriterion {
  targets: string[]
  dnsMode: "default" | "custom" | "skip"
  dns: string[]
  dnsMembers: Record<string, string[]>
}

export interface RoutingCriterion extends EntryCriterion {
  id: number
  name: string
}

export interface EntryDraft {
  name: string
  isEnabled: boolean
  action: RuleAction
  outbound: string
  ruleIds: number[]
  clients: string[]
  inbounds: string[]
  ports: string[]
  sourcePorts: string[]
  protocol: RuleProtocol
  criterion?: EntryCriterion
}

export interface RoutingEntry extends EntryDraft {
  id: number
  isDefault?: boolean
  criteria?: RoutingCriterion[]
  position: number
  state: RuleState
  createdUtc: string
  updatedUtc: string
}

export interface RouteQuestion {
  target: string
  port: number | null
  protocol: "tcp" | "udp"
  client: string
  sourcePort: number | null
}

export type RouteOutcome = "match" | "miss" | "skip"

export interface RouteStep {
  rule: number
  name: string
  outcome: RouteOutcome
  reason: string
  detail: string
}

export interface RouteMember {
  name: string
  isEnabled: boolean
  carries: boolean
  isPicked: boolean
}

export type RouteVerdict = "out" | "host" | "block" | "held" | "guard" | "dns"

export interface RouteAnswer {
  verdict: RouteVerdict
  guard: string
  name: string
  addresses: string[]
  inbound: string
  sources: string[]
  rule: { id: number; name: string; place: number; action: RuleAction; outbound: string } | null
  exit: { name: string; isGroup: boolean; strategy: string; members: RouteMember[] } | null
  steps: RouteStep[]
}


export function useRoutingEntries() {
  return useQuery({ queryKey: ["routing-entries"], queryFn: async () => (await client.get<RoutingEntry[]>("/routing-entries/table")).data })
}

export function useInternetOnlyProfile() {
  return useRefreshing((request: { interfaceIds: number[]; name: string; addressesName: string }) =>
    client.post<{ added: boolean }>("/routing-entries/profiles/internet-only", request).then(({ data }) => data))
}

export function useAddEntry() {
  return useRefreshing((draft: EntryDraft) => client.post("/routing-entries", entryPayload(draft)))
}

export function useChangeEntry() {
  return useRefreshing(({ id, draft }: { id: number; draft: EntryDraft }) => client.put(`/routing-entries/${id}`, entryPayload(draft)))
}

export function useRemoveEntry() {
  return useRefreshing((id: number) => client.delete(`/routing-entries/${id}`))
}

export function useMoveEntry() {
  return useRefreshing(({ id, up }: { id: number; up: boolean }) => client.post(`/routing-entries/${id}/move`, { up }))
}

export function usePlaceEntry() {
  return useRefreshing(({ id, to }: { id: number; to: number }) => client.post(`/routing-entries/${id}/move`, { up: false, to }))
}

export function useSwitchEntry() {
  return useRefreshing(({ id, on }: { id: number; on: boolean }) => client.post(`/routing-entries/${id}/switch`, { on }))
}

export function useRouteTest() {
  return useMutation({ mutationFn: async (question: RouteQuestion) => (await client.post<RouteAnswer>("/routing-entries/test", question)).data })
}

export function entryDraft(entry: RoutingEntry): EntryDraft {
  const stored = entry.criteria?.length === 1 ? entry.criteria[0] : undefined
  const criterion = stored ? { targets: stored.targets, dnsMode: stored.dnsMode, dns: stored.dns, dnsMembers: stored.dnsMembers ?? {} }
    : entry.isDefault ? freshCriterion : undefined

  return { name: entry.name, isEnabled: entry.isEnabled, action: entry.action, outbound: entry.outbound,
    ruleIds: entry.ruleIds, clients: entry.clients, inbounds: entry.inbounds, ports: entry.ports,
    sourcePorts: entry.sourcePorts, protocol: entry.protocol, criterion }
}

const freshCriterion: EntryCriterion = { targets: [], dnsMode: "default", dns: [], dnsMembers: {} }

function entryPayload(draft: EntryDraft) {
  if (draft.criterion === undefined) {
    return draft
  }

  const { ruleIds: _ruleIds, ...payload } = draft
  return payload
}

export const freshEntry: EntryDraft = {
  name: "", isEnabled: true, action: "direct", outbound: "", ruleIds: [], clients: [], inbounds: [], ports: [], sourcePorts: [], protocol: "any",
  criterion: freshCriterion,
}

function useRefreshing<T, TResult>(call: (args: T) => Promise<TResult>) {
  const queries = useQueryClient()

  return useMutation({ mutationFn: call, onSuccess: async () => {
    await queries.invalidateQueries({ queryKey: ["routing-entries"] })
    await queries.invalidateQueries({ queryKey: ["traffic-rules"] })
    await queries.invalidateQueries({ queryKey: ["dns-conflicts"] })
  } })
}
