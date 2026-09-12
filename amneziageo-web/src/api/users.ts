import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface User {
  name: string
  displayName: string
  kind: string
  role: string
  enabled: boolean
  hasPassword: boolean
  scopes: string[]
  hostUser: string
  hasKey: boolean
}

export interface UserDraft {
  name: string
  displayName: string
  role: string
  password: string
  mustChangePassword: boolean
  host: boolean
  publicKey: string
}

export interface UserChange {
  role?: string
  enabled?: boolean
  publicKey?: string
}

export function useUsers(enabled: boolean) {
  return useQuery({
    queryKey: ["users"],
    queryFn: async () => (await client.get<User[]>("/users")).data,
    enabled,
  })
}

export function useAddUser() {
  return useRefreshing((draft: UserDraft) => client.post("/users", draft))
}

export function useChangeUser() {
  return useRefreshing(({ name, change }: { name: string; change: UserChange }) =>
    client.patch(`/users/${encodeURIComponent(name)}`, change),
  )
}

export function useSetPassword() {
  return useRefreshing(({ name, password, mustChangePassword }: { name: string; password: string; mustChangePassword: boolean }) =>
    client.put(`/users/${encodeURIComponent(name)}/password`, { password, mustChangePassword }),
  )
}

export function useRemoveUser() {
  return useRefreshing((name: string) => client.delete(`/users/${encodeURIComponent(name)}`))
}

export function fresh(length = 20): string {
  const alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789"
  const numbers = new Uint32Array(length)
  crypto.getRandomValues(numbers)

  return Array.from(numbers, (value) => alphabet[value % alphabet.length]).join("")
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["users"] }),
  })
}
