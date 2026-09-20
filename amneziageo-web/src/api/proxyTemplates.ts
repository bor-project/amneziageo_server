import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ConfigSync } from "./configs"
import type { ProxyKind } from "./proxies"
import { client } from "./client"

export interface ProxyTemplate {
  id: number
  name: string
  kind: ProxyKind
  port: number
  opened: boolean
  makePath: boolean
  target: string
  sources: string[]
  proxies: number
  createdUtc: string
  updatedUtc: string
}

export interface ProxyTemplateDraft {
  name: string
  kind: ProxyKind
  port: number
  opened: boolean
  makePath: boolean
  target: string
  sources: string[]
}

export interface ProxyTemplateSave {
  template: ProxyTemplate
  proxies: ConfigSync[]
}

export function useProxyTemplates() {
  return useQuery({
    queryKey: ["proxy-templates"],
    queryFn: async () => (await client.get<ProxyTemplate[]>("/templates/proxies")).data,
  })
}

export function useFreshProxyTemplate(enabled: boolean) {
  return useQuery({
    queryKey: ["proxy-templates", "draft"],
    queryFn: async () => (await client.get<ProxyTemplate>("/templates/proxies/draft")).data,
    enabled,
    gcTime: 0,
    staleTime: 0,
  })
}

export function useAddProxyTemplate() {
  return useSpreading(
    async (draft: ProxyTemplateDraft) => (await client.post<ProxyTemplate>("/templates/proxies", draft)).data,
  )
}

export function useChangeProxyTemplate() {
  return useSpreading(
    async ({ id, draft }: { id: number; draft: ProxyTemplateDraft }) =>
      (await client.put<ProxyTemplateSave>(`/templates/proxies/${id}`, draft)).data,
  )
}

export function useRemoveProxyTemplate() {
  return useSpreading((id: number) => client.delete(`/templates/proxies/${id}`))
}

export function draftOf(template: ProxyTemplate): ProxyTemplateDraft {
  return {
    name: template.name,
    kind: template.kind,
    port: template.port,
    opened: template.opened,
    makePath: template.makePath,
    target: template.target,
    sources: template.sources,
  }
}

function useSpreading<TArgs, TResult>(call: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["proxy-templates"] })
      await queryClient.invalidateQueries({ queryKey: ["proxies"] })
    },
  })
}
