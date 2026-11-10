import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface AddressGroup {
  id: number
  name: string
  targets: string[]
  dns: string[]
  rules: number[]
}

export interface AddressGroupDraft {
  name: string
  targets: string[]
  dns: string[]
}

export function useAddressGroups() {
  return useQuery({
    queryKey: ["address-groups"],
    queryFn: async () => (await client.get<AddressGroup[]>("/address-groups")).data,
  })
}

export function useAddAddressGroup() {
  return useRefreshing((draft: AddressGroupDraft) => client.post("/address-groups", draft))
}

export function useChangeAddressGroup() {
  return useRefreshing(({ id, draft }: { id: number; draft: AddressGroupDraft }) => client.put("/address-groups/" + id, draft))
}

export function useRemoveAddressGroup() {
  return useRefreshing((id: number) => client.delete("/address-groups/" + id))
}

function useRefreshing<T>(call: (value: T) => Promise<unknown>) {
  const queries = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queries.invalidateQueries({ queryKey: ["address-groups"] })
      await queries.invalidateQueries({ queryKey: ["rules"] })
      await queries.invalidateQueries({ queryKey: ["dns-conflicts"] })
    },
  })
}
