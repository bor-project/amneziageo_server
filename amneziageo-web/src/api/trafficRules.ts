import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface TrafficRuleDraft {
  name: string
  targets: string[]
  dnsMode: "default" | "custom"
  dns: string[]
  dnsMembers: Record<string, string[]>
}

export interface TrafficRule extends TrafficRuleDraft {
  id: number
  createdUtc: string
  updatedUtc: string
}

export function useTrafficRules() {
  return useQuery({ queryKey: ["traffic-rules"], queryFn: async () => (await client.get<TrafficRule[]>("/traffic-rules")).data })
}

export function useFreshTrafficRule() {
  return useQuery({ queryKey: ["traffic-rules", "draft"], queryFn: async () => (await client.get<TrafficRuleDraft>("/traffic-rules/draft")).data, staleTime: 0, gcTime: 0 })
}

export function useAddTrafficRule() {
  return useRefreshing((draft: TrafficRuleDraft) => client.post<TrafficRule>("/traffic-rules", draft))
}

export function useChangeTrafficRule() {
  return useRefreshing(({ id, draft }: { id: number; draft: TrafficRuleDraft }) => client.put(`/traffic-rules/${id}`, draft))
}

export function useRemoveTrafficRule() {
  return useRefreshing((id: number) => client.delete(`/traffic-rules/${id}`))
}

export function trafficDraft(rule: TrafficRule): TrafficRuleDraft {
  return { name: rule.name, targets: rule.targets, dnsMode: rule.dnsMode ?? (rule.dns.length > 0 ? "custom" : "default"), dns: rule.dns, dnsMembers: rule.dnsMembers ?? {} }
}

function useRefreshing<T, TAnswer>(call: (args: T) => Promise<TAnswer>) {
  const queries = useQueryClient()

  return useMutation({ mutationFn: call, onSuccess: async () => {
    await queries.invalidateQueries({ queryKey: ["traffic-rules"] })
    await queries.invalidateQueries({ queryKey: ["routing-entries"] })
    await queries.invalidateQueries({ queryKey: ["dns-conflicts"] })
  } })
}

export function useDnsDefaults() {
  return useQuery({ queryKey: ["dns-defaults"], queryFn: async () => (await client.get<{ servers: string[] }>("/traffic-rules/dns-defaults")).data, refetchInterval: 15000 })
}
