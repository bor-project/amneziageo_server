import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export type RuleAction = "out" | "block"

export type RuleProtocol = "any" | "tcp" | "udp"

export interface RuleState {
  isLive: boolean
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
  targets: string[]
  sources: string[]
  ports: string[]
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
  targets: string[]
  sources: string[]
  ports: string[]
  protocol: RuleProtocol
}

export function useRules() {
  return useQuery({
    queryKey: ["rules"],
    queryFn: async () => (await client.get<Rule[]>("/rules")).data,
  })
}

export function useRuleset(enabled: boolean) {
  return useQuery({
    queryKey: ["rules", "ruleset"],
    queryFn: async () => (await client.get<{ text: string }>("/rules/ruleset")).data.text,
    enabled,
    gcTime: 0,
    staleTime: 0,
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

export function useSwitchRule() {
  return useRefreshing(({ id, on }: { id: number; on: boolean }) => client.post(`/rules/${id}/switch`, { on }))
}

export function useApplyRules() {
  return useRefreshing(() => client.post("/rules/apply"))
}

export function draftOf(rule: Rule): RuleDraft {
  return {
    name: rule.name,
    isEnabled: rule.isEnabled,
    action: rule.action,
    outbound: rule.outbound,
    targets: rule.targets,
    sources: rule.sources,
    ports: rule.ports,
    protocol: rule.protocol,
  }
}

export const freshRule: RuleDraft = {
  name: "",
  isEnabled: true,
  action: "out",
  outbound: "",
  targets: [],
  sources: [],
  ports: [],
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
