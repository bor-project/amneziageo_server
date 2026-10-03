import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface Preset {
  id: number
  name: string
  proxy: string[]
  direct: string[]
  block: string[]
  allUdp: boolean
  full: boolean
  templates: number
  createdUtc: string
  updatedUtc: string
}

export interface PresetDraft {
  name: string
  proxy: string[]
  direct: string[]
  block: string[]
  allUdp: boolean
  full: boolean
}

export function usePresets() {
  return useQuery({
    queryKey: ["presets"],
    queryFn: async () => (await client.get<Preset[]>("/templates/routing")).data,
  })
}

export function useAddPreset() {
  return useRefreshing(async (draft: PresetDraft) => (await client.post<Preset>("/templates/routing", draft)).data)
}

export function useChangePreset() {
  return useRefreshing(({ id, draft }: { id: number; draft: PresetDraft }) =>
    client.put(`/templates/routing/${id}`, draft),
  )
}

export function useRemovePreset() {
  return useRefreshing((id: number) => client.delete(`/templates/routing/${id}`))
}

export function draftOf(preset: Preset): PresetDraft {
  return {
    name: preset.name,
    proxy: preset.proxy,
    direct: preset.direct,
    block: preset.block,
    allUdp: preset.allUdp,
    full: preset.full,
  }
}

export const freshPreset: PresetDraft = {
  name: "",
  proxy: [],
  direct: [],
  block: [],
  allUdp: false,
  full: false,
}

function useRefreshing<TArgs, TResult>(call: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["presets"] })
      await queryClient.invalidateQueries({ queryKey: ["templates"] })
    },
  })
}
