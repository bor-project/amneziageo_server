import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface ApiToken {
  id: number
  name: string
  role: string
  createdUtc: string
  expiresUtc: string | null
  lastUsedUtc: string | null
  lastAddress: string | null
  isExpired: boolean
}

export interface ApiTokenDraft {
  name: string
  role: string
  days: number | null
}

export interface MintedApiToken {
  token: ApiToken
  secret: string
}

export const maxTokenDays = 3650

export function useApiTokens() {
  return useQuery({
    queryKey: ["api-tokens"],
    queryFn: async () => (await client.get<ApiToken[]>("/tokens")).data,
  })
}

export function useMintApiToken() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (draft: ApiTokenDraft) => (await client.post<MintedApiToken>("/tokens", draft)).data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["api-tokens"] }),
  })
}

export function useRevokeApiToken() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: number) => client.delete(`/tokens/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["api-tokens"] }),
  })
}
