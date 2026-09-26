import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface ClientState {
  isOnline: boolean
  isPresent: boolean
  lastHandshake: string | null
  rxBytes: number
  txBytes: number
  endpoint: string
  cut: string[]
  rxRate: number
  txRate: number
  todayRx: number
  todayTx: number
  used: number
  isSpent: boolean
}

export type Inbound = "off" | "server" | "network" | "endpoint"

export type Routing = "template" | "on" | "off"

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
  templateId: number | null
  subscriptionId: string
  dailyLimit: number
  inbound: Inbound
  routing: Routing
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
  templateId: number | null
  subscriptionId: string
  dailyLimit: number
  inbound: Inbound
  routing: Routing
}

export type Miss = "off" | "no-id" | "no-key"

export interface ClientConfig {
  fileName: string
  text: string
  link: string
  subscription: string
  subscriptionMiss: Miss | ""
}

export function useClients() {
  return useQuery({
    queryKey: ["clients"],
    queryFn: async () => (await client.get<Client[]>("/clients")).data,
    refetchInterval: 2000,
  })
}

export function useClientConfig(id: number | null) {
  return useQuery({
    queryKey: ["client-config", id],
    queryFn: async () => (await client.get<ClientConfig>(`/clients/${id}/config`)).data,
    enabled: id !== null,
  })
}

export function useClientDraft() {
  return useQuery({
    queryKey: ["client-draft"],
    queryFn: async () => (await client.get<Client>("/clients/draft")).data,
    gcTime: 0,
    staleTime: 0,
  })
}

export function useAddClient() {
  return useRefreshing(async (draft: ClientDraft) => (await client.post<Client>("/clients", draft)).data)
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

export interface ClientBatch {
  done: number[]
  failed: { id: number; code: string; message: string }[]
  unsynced: { config: string; message: string }[]
}

export function useSwitchClients() {
  return useRefreshing(
    async ({ ids, on }: { ids: number[]; on: boolean }) =>
      (await client.post<ClientBatch>("/clients/switch", { ids, on })).data,
  )
}

export function useRemoveClients() {
  return useRefreshing(async (ids: number[]) => (await client.post<ClientBatch>("/clients/remove", { ids })).data)
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
    templateId: one.templateId,
    subscriptionId: one.subscriptionId,
    dailyLimit: one.dailyLimit,
    inbound: one.inbound,
    routing: one.routing,
  }
}

function useRefreshing<TArgs, TResult>(call: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["clients"] })
      await queryClient.invalidateQueries({ queryKey: ["templates"] })
      await queryClient.invalidateQueries({ queryKey: ["client-config"] })
    },
  })
}
