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

export interface Rule {
  id: number
  name: string
  position: number
  isEnabled: boolean
  action: RuleAction
  outbound: string
  holdsWhenDown: boolean
  targets: string[]
  sources: string[]
  clients: string[]
  inbounds: string[]
  ports: string[]
  sourcePorts: string[]
  protocol: RuleProtocol
  state: RuleState
  createdUtc: string
  updatedUtc: string
}

export interface RuleDraft {
  name: string
  isEnabled: boolean
  action: RuleAction
  outbound: string
  holdsWhenDown: boolean
  targets: string[]
  sources: string[]
  clients: string[]
  inbounds: string[]
  ports: string[]
  sourcePorts: string[]
  protocol: RuleProtocol
}

export interface BasicLists {
  direct: string[]
  block: string[]
  directState: RuleState
  blockState: RuleState
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

export const blockList = -1

export const directList = -2

export function useRules() {
  return useQuery({
    queryKey: ["rules"],
    queryFn: async () => (await client.get<Rule[]>("/rules")).data,
  })
}

export function useAddRule() {
  return useRefreshing((draft: RuleDraft) => client.post("/rules", draft))
}

export function useChangeRule() {
  return useRefreshing(({ id, draft }: { id: number; draft: RuleDraft }) => client.put(`/rules/${id}`, draft))
}

export function useRemoveRule() {
  return useRefreshing((id: number) => client.delete(`/rules/${id}`))
}

export function useMoveRule() {
  return useRefreshing(({ id, up }: { id: number; up: boolean }) => client.post(`/rules/${id}/move`, { up }))
}

export function usePlaceRule() {
  return useRefreshing(({ id, to }: { id: number; to: number }) => client.post(`/rules/${id}/move`, { up: false, to }))
}

export function useBasic() {
  return useQuery({
    queryKey: ["rules", "basic"],
    queryFn: async () => (await client.get<BasicLists>("/rules/basic")).data,
  })
}

export function useSaveBasic() {
  return useRefreshing((lists: { direct: string[]; block: string[] }) => client.put("/rules/basic", lists))
}

export function useRuleset() {
  return useQuery({
    queryKey: ["rules", "ruleset"],
    queryFn: async () => (await client.get<{ text: string }>("/rules/ruleset")).data.text,
  })
}

export function useRouteTest() {
  return useMutation({
    mutationFn: async (question: RouteQuestion) => (await client.post<RouteAnswer>("/rules/test", question)).data,
  })
}

export function useSwitchRule() {
  return useRefreshing(({ id, on }: { id: number; on: boolean }) => client.post(`/rules/${id}/switch`, { on }))
}

export function draftOf(rule: Rule): RuleDraft {
  return {
    name: rule.name,
    isEnabled: rule.isEnabled,
    action: rule.action,
    outbound: rule.outbound,
    holdsWhenDown: rule.holdsWhenDown,
    targets: rule.targets,
    sources: rule.sources,
    clients: rule.clients,
    inbounds: rule.inbounds,
    ports: rule.ports,
    sourcePorts: rule.sourcePorts,
    protocol: rule.protocol,
  }
}

export const freshRule: RuleDraft = {
  name: "",
  isEnabled: true,
  action: "out",
  outbound: "",
  holdsWhenDown: true,
  targets: [],
  sources: [],
  clients: [],
  inbounds: [],
  ports: [],
  sourcePorts: [],
  protocol: "any",
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["rules"] })
    },
  })
}
