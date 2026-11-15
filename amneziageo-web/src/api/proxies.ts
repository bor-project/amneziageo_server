import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface ProxyDestination { host: string; port: number }
export interface ProxyTargets { restricted: boolean; destinations: ProxyDestination[]; local?: boolean }

export interface ProxyDraft {
  name: string
  transport: "websocket" | "http" | "quic" | "xhttp"
  host: string
  port: number
  path: string
  listenPort: number
  hosted: boolean
  originHttp: boolean
  useCertificate: boolean | null
  httpMethod: "GET" | "POST"
  protocolVersion: number
  isEnabled: boolean
  allowAnonymous: boolean
  opened: boolean
  certificate: string
  certificateKey: string
  serverName: string
  pin: string
  targets?: ProxyTargets | null
}

export interface TransportProxy extends ProxyDraft {
  id: number
  legacyConfigId: number | null
  fault: string
}

export function useProxies() {
  return useQuery({ queryKey: ["proxies"], queryFn: async () => (await client.get<TransportProxy[]>("/proxies")).data })
}

export function useProxyDraft(enabled: boolean) {
  return useQuery({ queryKey: ["proxies", "draft"], queryFn: async () => (await client.get<TransportProxy>("/proxies/draft")).data, enabled })
}

export function useSaveProxy() {
  const query = useQueryClient()
  return useMutation({
    mutationFn: ({ id, draft }: { id: number; draft: ProxyDraft & { keep?: boolean } }) => id === 0 ? client.post("/proxies", draft) : client.put(`/proxies/${id}`, draft),
    onSuccess: async () => {
      await query.invalidateQueries({ queryKey: ["proxies"] })
      await query.invalidateQueries({ queryKey: ["panel"] })
    },
  })
}

export function useRemoveProxy() {
  const query = useQueryClient()
  return useMutation({ mutationFn: (id: number) => client.delete(`/proxies/${id}`), onSuccess: async () => {
    await query.invalidateQueries({ queryKey: ["proxies"] })
    await query.invalidateQueries({ queryKey: ["templates"] })
    await query.invalidateQueries({ queryKey: ["panel"] })
  } })
}
