import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export type ProxyKind = "ws" | "wg"

export interface ProxyDraft {
  name: string
  kind: ProxyKind
  isEnabled: boolean
  port: number
  opened: boolean
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

export function uncertified(panel: ProxyCertificate | undefined, proxy: ProxyDraft): boolean {
  const bare = panel !== undefined && (panel.chain.length === 0 || panel.key.length === 0)

  return bare && proxy.kind === "ws" && (proxy.certificate.length === 0 || proxy.certificateKey.length === 0)
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

export function useFreshProxy(enabled: boolean, name: string, kind: ProxyKind) {
  return useQuery({
    queryKey: ["proxies", "draft", name, kind],
    queryFn: async () => (await client.get<Proxy>(`/proxies/draft?name=${name}&kind=${kind}`)).data,
    enabled,
  })
}

export function useAddProxy() {
  return useRefreshing(async (draft: ProxyDraft) => (await client.post<Proxy>("/proxies", draft)).data)
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
    opened: proxy.opened,
    path: proxy.path,
    target: proxy.target,
    sources: proxy.sources,
    certificate: proxy.certificate,
    certificateKey: proxy.certificateKey,
  }
}

function useRefreshing<TArgs, TResult>(call: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["proxies"] })
    },
  })
}
