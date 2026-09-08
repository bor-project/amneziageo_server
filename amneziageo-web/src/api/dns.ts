import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface DnsState {
  isRunning: boolean
  listening: string[]
  fault: string
  questions: number
  cached: number
  failed: number
  added: number
  waiting: number
  names: number
  startedUtc: string | null
}

export interface DnsDraft {
  isEnabled: boolean
  port: number
  upstreams: string[]
  listen: string[]
  nameMinutes: number
  cacheSize: number
  minTtl: number
  maxTtl: number
  intercept: boolean
  blockDot: boolean
  blockDoh: boolean
}

export interface Resolver extends DnsDraft {
  state: DnsState
}

export function useResolver() {
  return useQuery({
    queryKey: ["dns"],
    queryFn: async () => (await client.get<Resolver>("/dns")).data,
    refetchInterval: 5000,
  })
}

export function useSaveResolver() {
  return useRefreshing((draft: DnsDraft) => client.put("/dns", draft))
}

export function useRestartResolver() {
  return useRefreshing(() => client.post("/dns/restart"))
}

export function draftOf(resolver: Resolver): DnsDraft {
  return {
    isEnabled: resolver.isEnabled,
    port: resolver.port,
    upstreams: resolver.upstreams,
    listen: resolver.listen,
    nameMinutes: resolver.nameMinutes,
    cacheSize: resolver.cacheSize,
    minTtl: resolver.minTtl,
    maxTtl: resolver.maxTtl,
    intercept: resolver.intercept,
    blockDot: resolver.blockDot,
    blockDoh: resolver.blockDoh,
  }
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["dns"] })
      await queryClient.invalidateQueries({ queryKey: ["rules"] })
    },
  })
}
