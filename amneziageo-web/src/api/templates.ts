import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface Template {
  id: number
  name: string
  entries: string[]
  allowedIps: string[]
  missed: string[]
  dns: string[]
  mtu: number | null
  keepalive: number | null
  routing: boolean
  clients: number
  refreshedUtc: string | null
  createdUtc: string
  updatedUtc: string
}

export interface TemplateDraft {
  name: string
  entries: string[]
  dns: string[]
  mtu: number | null
  keepalive: number | null
  routing: boolean
}

export interface TemplatePart {
  entry: string
  total: number
  allowedIps: string[]
}

export interface TemplatePreview {
  total: number
  allowedIps: string[]
  missed: string[]
  parts: TemplatePart[]
}

export interface TemplateDefaults {
  allowedIps: string[]
  dns: string[]
  mtu: number
  keepalive: number
  routing: boolean
}

const slow = { timeout: 120000 }

export function useTemplates() {
  return useQuery({
    queryKey: ["templates"],
    queryFn: async () => (await client.get<Template[]>("/templates")).data,
  })
}

export function useTemplateDefaults() {
  return useQuery({
    queryKey: ["template-defaults"],
    queryFn: async () => (await client.get<TemplateDefaults>("/templates/defaults")).data,
  })
}

export function useTemplatePreview(entries: string[]) {
  return useQuery({
    queryKey: ["template-preview", entries],
    queryFn: async () => (await client.post<TemplatePreview>("/templates/preview", { entries }, slow)).data,
    enabled: entries.length > 0,
    staleTime: 60000,
  })
}

export function useAddTemplate() {
  return useRefreshing(async (draft: TemplateDraft) => (await client.post<Template>("/templates", draft, slow)).data)
}

export function useChangeTemplate() {
  return useRefreshing(({ id, draft }: { id: number; draft: TemplateDraft }) =>
    client.put(`/templates/${id}`, draft, slow),
  )
}

export function useRefreshTemplate() {
  return useRefreshing((id: number) => client.post(`/templates/${id}/refresh`, null, slow))
}

export function useRemoveTemplate() {
  return useRefreshing((id: number) => client.delete(`/templates/${id}`))
}

export function draftOf(template: Template): TemplateDraft {
  return {
    name: template.name,
    entries: template.entries,
    dns: template.dns,
    mtu: template.mtu,
    keepalive: template.keepalive,
    routing: template.routing,
  }
}

export const freshTemplate: TemplateDraft = {
  name: "",
  entries: [],
  dns: [],
  mtu: null,
  keepalive: null,
  routing: true,
}

function useRefreshing<TArgs, TResult>(call: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["templates"] })
      await queryClient.invalidateQueries({ queryKey: ["client-config"] })
    },
  })
}
