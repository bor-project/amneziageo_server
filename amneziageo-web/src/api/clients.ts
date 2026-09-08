import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface ClientState {
  isOnline: boolean
  isPresent: boolean
  lastHandshake: string | null
  rxBytes: number
  txBytes: number
  endpoint: string
}

export interface Client {
  id: number
  configId: number
  config: string
  name: string
  publicKey: string
  privateKey: string
  presharedKey: string
  address: string[]
  isEnabled: boolean
  note: string
  state: ClientState
  createdUtc: string
  updatedUtc: string
}

export interface ClientDraft {
  configId: number
  name: string
  privateKey: string
  publicKey: string
  presharedKey: string
  address: string[]
  isEnabled: boolean
  note: string
}

export interface ClientConfig {
  fileName: string
  text: string
}

export interface ClientApply {
  config: string
  isDone: boolean
  clients: number
  stale: string[]
  message: string
}

export function useClients() {
  return useQuery({
    queryKey: ["clients"],
    queryFn: async () => (await client.get<Client[]>("/clients")).data,
    refetchInterval: 15000,
  })
}

export function useClientConfig(id: number | null) {
  return useQuery({
    queryKey: ["client-config", id],
    queryFn: async () => (await client.get<ClientConfig>(`/clients/${id}/config`)).data,
    enabled: id !== null,
  })
}

export function useClientDraft(configId: number | null) {
  return useQuery({
    queryKey: ["client-draft", configId],
    queryFn: async () => (await client.get<Client>(`/clients/draft?config=${configId}`)).data,
    enabled: configId !== null,
    gcTime: 0,
    staleTime: 0,
  })
}

export function useAddClient() {
  return useRefreshing((draft: ClientDraft) => client.post("/clients", draft))
}

export function useChangeClient() {
  return useRefreshing(({ id, draft }: { id: number; draft: ClientDraft }) => client.put(`/clients/${id}`, draft))
}

export function useRemoveClient() {
  return useRefreshing((id: number) => client.delete(`/clients/${id}`))
}

export function useSwitchClient() {
  return useRefreshing(({ id, on }: { id: number; on: boolean }) => client.post(`/clients/${id}/switch`, { on }))
}

export function useApplyClients() {
  return useRefreshing(() => client.post<ClientApply[]>("/clients/apply"))
}

export function draftOf(one: Client): ClientDraft {
  return {
    configId: one.configId,
    name: one.name,
    privateKey: one.privateKey,
    publicKey: one.publicKey,
    presharedKey: one.presharedKey,
    address: one.address,
    isEnabled: one.isEnabled,
    note: one.note,
  }
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["clients"] })
    },
  })
}
