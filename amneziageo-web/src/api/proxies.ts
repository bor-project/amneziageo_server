import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export type ProxyKind = "ws" | "wg"

export interface ProxyDraft {
  name: string
  kind: ProxyKind
  isEnabled: boolean
  port: number
  path: string
  target: string
  sources: string[]
  certificate: string
  certificateKey: string
}

export interface Proxy extends ProxyDraft {
  id: number
  isRunning: boolean
  message: string
}

export interface ProxyCertificate {
  chain: string
  key: string
}

export function useProxyCertificate() {
  return useQuery({
    queryKey: ["proxies", "certificate"],
    queryFn: async () => (await client.get<ProxyCertificate>("/proxies/certificate")).data,
  })
}

export function useProxyAddresses() {
  return useQuery({
    queryKey: ["proxies", "addresses"],
    queryFn: async () => (await client.get<string[]>("/proxies/addresses")).data,
  })
}

export function useProxies() {
  return useQuery({
    queryKey: ["proxies"],
    queryFn: async () => (await client.get<Proxy[]>("/proxies")).data,
  })
}

export function useFreshProxy(enabled: boolean, name: string) {
  return useQuery({
    queryKey: ["proxies", "draft", name],
    queryFn: async () => (await client.get<Proxy>(`/proxies/draft?name=${name}`)).data,
    enabled,
  })
}

export function useAddProxy() {
  return useRefreshing((draft: ProxyDraft) => client.post("/proxies", draft))
}

export function useChangeProxy() {
  return useRefreshing(({ id, draft }: { id: number; draft: ProxyDraft }) => client.put(`/proxies/${id}`, draft))
}

export function useSwitchProxy() {
  return useRefreshing(({ id, on }: { id: number; on: boolean }) => client.post(`/proxies/${id}/switch`, { on }))
}

export function useRemoveProxy() {
  return useRefreshing((id: number) => client.delete(`/proxies/${id}`))
}

export function draftOf(proxy: Proxy): ProxyDraft {
  return {
    name: proxy.name,
    kind: proxy.kind,
    isEnabled: proxy.isEnabled,
    port: proxy.port,
    path: proxy.path,
    target: proxy.target,
    sources: proxy.sources,
    certificate: proxy.certificate,
    certificateKey: proxy.certificateKey,
  }
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["proxies"] })
    },
  })
}
