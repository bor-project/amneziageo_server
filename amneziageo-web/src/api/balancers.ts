import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export type BalanceStrategy = "priority" | "round" | "sticky"

export interface BalancerMember {
  name: string
  isKnown: boolean
  isEnabled: boolean
  isAlive: boolean
  mark: number
}

export interface BalancerState {
  isLive: boolean
  members: BalancerMember[]
}

export interface Balancer {
  id: number
  name: string
  position: number
  isEnabled: boolean
  strategy: BalanceStrategy
  members: string[]
  state: BalancerState
  createdUtc: string
  updatedUtc: string
}

export interface BalancerDraft {
  name: string
  isEnabled: boolean
  strategy: BalanceStrategy
  members: string[]
}

export function useBalancers() {
  return useQuery({
    queryKey: ["balancers"],
    queryFn: async () => (await client.get<Balancer[]>("/balancers")).data,
    refetchInterval: 15000,
  })
}

export function useAddBalancer() {
  return useRefreshing((draft: BalancerDraft) => client.post("/balancers", draft))
}

export function useChangeBalancer() {
  return useRefreshing(({ id, draft }: { id: number; draft: BalancerDraft }) =>
    client.put(`/balancers/${id}`, draft),
  )
}

export function useRemoveBalancer() {
  return useRefreshing((id: number) => client.delete(`/balancers/${id}`))
}

export function useSwitchBalancer() {
  return useRefreshing(({ id, on }: { id: number; on: boolean }) =>
    client.post(`/balancers/${id}/switch`, { on }),
  )
}

export function draftOf(balancer: Balancer): BalancerDraft {
  return {
    name: balancer.name,
    isEnabled: balancer.isEnabled,
    strategy: balancer.strategy,
    members: balancer.members,
  }
}

export const freshBalancer: BalancerDraft = {
  name: "",
  isEnabled: true,
  strategy: "priority",
  members: [],
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["balancers"] })
      await queryClient.invalidateQueries({ queryKey: ["rules"] })
    },
  })
}
