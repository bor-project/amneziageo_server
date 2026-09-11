import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
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
  blocked: string[]
  publicKey: string
  privateKey: string | null
  presharedKey: string | null
  obfuscation: Obfuscation
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
  blocked: string[]
  privateKey: string
  presharedKey: string
  obfuscation: Obfuscation
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
  return useRefreshing((draft: ConfigDraft) => client.post("/configs", draft))
}

export function useChangeConfig() {
  return useRefreshing(({ id, draft }: { id: number; draft: ConfigDraft }) =>
    client.put(`/configs/${id}`, draft),
  )
}

export function useRemoveConfig() {
  return useRefreshing((id: number) => client.delete(`/configs/${id}`))
}

export function useApplyConfig() {
  return useRefreshing((id: number) => client.post<ConfigSync>(`/configs/${id}/apply`))
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
    blocked: config.blocked,
    privateKey: config.privateKey ?? "",
    presharedKey: config.presharedKey ?? "",
    obfuscation: config.obfuscation,
  }
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["configs"] })
    },
  })
}
