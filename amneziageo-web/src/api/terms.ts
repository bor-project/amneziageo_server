import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface TermsAcceptance {
  version: number
  login: string
  acceptedUtc: string
}

export interface Terms {
  version: number
  accepted: boolean
  last: TermsAcceptance | null
}

export function useTerms() {
  return useQuery({
    queryKey: ["terms"],
    queryFn: async () => (await client.get<Terms>("/terms")).data,
  })
}

export function useAcceptTerms() {
  const queries = useQueryClient()

  return useMutation({
    mutationFn: async (version: number) => (await client.post<Terms>("/terms", { version })).data,
    onSuccess: (terms) => queries.setQueryData(["terms"], terms),
  })
}
