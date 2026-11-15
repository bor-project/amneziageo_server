import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface ProxyToken { id: string; name: string; createdUtc: string }
export interface MintedProxyToken { token: ProxyToken; secret: string }

export function useProxyTokens(id: number) {
  return useQuery({ queryKey: ["proxy-tokens", id], queryFn: async () => (await client.get<ProxyToken[]>(`/proxies/${id}/tokens`)).data, enabled: id > 0 })
}

export function useMintProxyToken(id: number) {
  const query = useQueryClient()
  return useMutation({ gcTime: 0, mutationFn: async (name: string) => (await client.post<MintedProxyToken>(`/proxies/${id}/tokens`, { name })).data,
    onSuccess: async () => { await query.invalidateQueries({ queryKey: ["proxy-tokens", id] }) } })
}

export function useRevokeProxyToken(id: number) {
  const query = useQueryClient()
  return useMutation({ mutationFn: async (tokenId: string) => { await client.delete(`/proxies/${id}/tokens/${tokenId}`) },
    onSuccess: async () => { await query.invalidateQueries({ queryKey: ["proxy-tokens", id] }) } })
}
