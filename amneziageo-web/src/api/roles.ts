import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface Role {
  name: string
  title: string
  builtin: boolean
  scopes: string[]
  users: number
}

export interface RoleList {
  roles: Role[]
  scopes: string[]
}

export interface RoleDraft {
  name: string
  title: string
  scopes: string[]
}

export interface RoleChange {
  title?: string
  scopes?: string[]
}

export function useRoles(enabled = true) {
  return useQuery({
    queryKey: ["roles"],
    queryFn: async () => (await client.get<RoleList>("/roles")).data,
    enabled,
  })
}

export function useAddRole() {
  return useRefreshing((draft: RoleDraft) => client.post("/roles", draft))
}

export function useChangeRole() {
  return useRefreshing(({ name, change }: { name: string; change: RoleChange }) =>
    client.patch(`/roles/${encodeURIComponent(name)}`, change),
  )
}

export function useRemoveRole() {
  return useRefreshing((name: string) => client.delete(`/roles/${encodeURIComponent(name)}`))
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["roles"] })
      await queryClient.invalidateQueries({ queryKey: ["users"] })
    },
  })
}
