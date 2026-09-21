import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import axios from "axios"
import type { Text } from "@/i18n"
import { complaint } from "./auth"
import type { Inbound } from "./clients"
import { client } from "./client"

export interface Obfuscation {
  jc: number
  jmin: number
  jmax: number
  s1: number
  s2: number
  s3: number
  s4: number
  h1: string
  h2: string
  h3: string
  h4: string
  i1: string | null
  i2: string | null
  i3: string | null
  i4: string | null
  i5: string | null
  headerProtectionKey: string
  contentPaddingAddition: string
  rekeyAfterTime: string
  rekeyTimeout: string
  rejectAfterTime: string
  keepaliveTimeout: string
  maxHandshakeAttempts: string
  randomTrailers: boolean
  disableCookies: boolean
}

export interface Config {
  id: number
  name: string
  host: string
  listenPort: number
  address: string[]
  dns: string[]
  allowedIps: string[]
  mtu: number
  keepalive: number
  offlineAfter: number
  isEnabled: boolean
  nat: boolean
  opened: boolean
  inbound: Inbound
  blocked: string[]
  publicKey: string
  privateKey: string | null
  presharedKey: string | null
  obfuscation: Obfuscation
  templateId: number | null
  createdUtc: string
  updatedUtc: string
}

export interface ConfigDraft {
  name: string
  host: string
  listenPort: number
  address: string[]
  dns: string[]
  allowedIps: string[]
  mtu: number
  keepalive: number
  offlineAfter: number
  isEnabled: boolean
  nat: boolean
  opened: boolean
  inbound: Inbound
  blocked: string[]
  privateKey: string
  presharedKey: string
  obfuscation: Obfuscation
  templateId: number | null
}

export interface ConfigSync {
  name: string
  isDone: boolean
  message: string
}

export interface KeyPair {
  privateKey: string
  publicKey: string
}

export interface SingleKey {
  key: string
}

export function useConfigs() {
  return useQuery({
    queryKey: ["configs"],
    queryFn: async () => (await client.get<Config[]>("/configs")).data,
  })
}

export function useFreshConfig(enabled: boolean, name: string) {
  return useQuery({
    queryKey: ["configs", "draft", name],
    queryFn: async () => (await client.get<Config>(`/configs/draft?name=${encodeURIComponent(name)}`)).data,
    enabled,
    gcTime: 0,
    staleTime: 0,
  })
}

export function useAddConfig() {
  return useRefreshing(async (draft: ConfigDraft) => (await client.post<Config>("/configs", draft)).data)
}

export function useChangeConfig() {
  return useRefreshing(({ id, draft }: { id: number; draft: ConfigDraft }) =>
    client.put(`/configs/${id}`, draft),
  )
}

export function useSwitchConfig() {
  return useRefreshing(({ id, on }: { id: number; on: boolean }) => client.post(`/configs/${id}/switch`, { on }))
}

export function useRemoveConfig() {
  return useRefreshing((id: number) => client.delete(`/configs/${id}`))
}

export function usePresharedKey() {
  return useMutation({
    mutationFn: async () => (await client.post<SingleKey>("/configs/preshared")).data,
  })
}

export function useKeyPair() {
  return useMutation({
    mutationFn: async () => (await client.post<KeyPair>("/configs/keys")).data,
  })
}

export function useImportConfig() {
  return useMutation({
    mutationFn: async ({ name, text }: { name: string; text: string }) =>
      (await client.post<Config>("/configs/import", { name, text })).data,
  })
}

export interface Downed {
  error: string
  message: string
  id: number
}

const downs = ["port-busy", "no-module", "no-rights", "forwarding-off", "raise-failed"]

export function downedOf(error: unknown): Downed | null {
  if (!axios.isAxiosError(error)) {
    return null
  }

  const said = error.response?.data as Partial<Downed> | undefined

  return said !== undefined && downs.includes(said.error ?? "") && typeof said.id === "number"
    ? { error: said.error ?? "", message: said.message ?? "", id: said.id }
    : null
}

export function failure(t: Text, error: unknown): string {
  const downed = downedOf(error)

  return downed?.error === "raise-failed" ? t("error.raiseFailed", { reason: downed.message }) : t(complaint(error))
}

export function draftOf(config: Config): ConfigDraft {
  return {
    name: config.name,
    host: config.host,
    listenPort: config.listenPort,
    address: config.address,
    dns: config.dns,
    allowedIps: config.allowedIps,
    mtu: config.mtu,
    keepalive: config.keepalive,
    offlineAfter: config.offlineAfter,
    isEnabled: config.isEnabled,
    nat: config.nat,
    opened: config.opened,
    inbound: config.inbound,
    blocked: config.blocked,
    privateKey: config.privateKey ?? "",
    presharedKey: config.presharedKey ?? "",
    obfuscation: config.obfuscation,
    templateId: config.templateId,
  }
}

function useRefreshing<TArgs, TResult>(call: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: ["configs"] })
    },
  })
}
