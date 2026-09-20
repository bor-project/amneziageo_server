import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ConfigSync, Obfuscation } from "./configs"
import { client } from "./client"

export interface InterfaceTemplate {
  id: number
  name: string
  listenPort: number
  subnet: string
  dns: string[]
  allowedIps: string[]
  mtu: number
  keepalive: number
  offlineAfter: number
  blocked: string[]
  clientTemplateId: number | null
  obfuscation: Obfuscation
  configs: number
  createdUtc: string
  updatedUtc: string
}

export interface InterfaceTemplateDraft {
  name: string
  listenPort: number
  subnet: string
  dns: string[]
  allowedIps: string[]
  mtu: number
  keepalive: number
  offlineAfter: number
  blocked: string[]
  clientTemplateId: number | null
  obfuscation: Obfuscation
}

export interface InterfaceTemplateSave {
  template: InterfaceTemplate
  configs: ConfigSync[]
}

export function useInterfaceTemplates() {
  return useQuery({
    queryKey: ["interface-templates"],
    queryFn: async () => (await client.get<InterfaceTemplate[]>("/templates/interfaces")).data,
  })
}

export function useFreshInterfaceTemplate(enabled: boolean) {
  return useQuery({
    queryKey: ["interface-templates", "draft"],
    queryFn: async () => (await client.get<InterfaceTemplate>("/templates/interfaces/draft")).data,
    enabled,
    gcTime: 0,
    staleTime: 0,
  })
}

export function useAddInterfaceTemplate() {
  return useSpreading(
    async (draft: InterfaceTemplateDraft) =>
      (await client.post<InterfaceTemplate>("/templates/interfaces", draft)).data,
  )
}

export function useChangeInterfaceTemplate() {
  return useSpreading(
    async ({ id, draft }: { id: number; draft: InterfaceTemplateDraft }) =>
      (await client.put<InterfaceTemplateSave>(`/templates/interfaces/${id}`, draft)).data,
  )
}

export function useRemoveInterfaceTemplate() {
  return useSpreading((id: number) => client.delete(`/templates/interfaces/${id}`))
}

export function draftOf(template: InterfaceTemplate): InterfaceTemplateDraft {
  return {
    name: template.name,
    listenPort: template.listenPort,
    subnet: template.subnet,
    dns: template.dns,
    allowedIps: template.allowedIps,
    mtu: template.mtu,
    keepalive: template.keepalive,
    offlineAfter: template.offlineAfter,
    blocked: template.blocked,
    clientTemplateId: template.clientTemplateId,
    obfuscation: template.obfuscation,
  }
}

function useSpreading<TArgs, TResult>(call: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["interface-templates"] })
      await queryClient.invalidateQueries({ queryKey: ["configs"] })
      await queryClient.invalidateQueries({ queryKey: ["clients"] })
      await queryClient.invalidateQueries({ queryKey: ["client-config"] })
    },
  })
}
