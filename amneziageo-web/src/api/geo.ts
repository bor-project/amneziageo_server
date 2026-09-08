import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export type GeoKind = "geoip" | "geosite"

export interface GeoSource {
  id: number
  name: string
  kind: GeoKind
  url: string
  position: number
  isEnabled: boolean
  updatedUtc: string | null
  sha256: string
  entryCount: number
  size: number
  lastError: string
}

export interface GeoSourceDraft {
  name: string
  kind: GeoKind
  url: string
  isEnabled: boolean
}

export interface GeoKeys {
  countries: string[]
  categories: string[]
}

export function useGeoSources() {
  return useQuery({
    queryKey: ["geo", "sources"],
    queryFn: async () => (await client.get<GeoSource[]>("/geo/sources")).data,
  })
}

export function useGeoKeys() {
  return useQuery({
    queryKey: ["geo", "keys"],
    queryFn: async () => (await client.get<GeoKeys>("/geo/keys")).data,
  })
}

export function useAddGeoSource() {
  return useRefreshing((draft: GeoSourceDraft) => client.post("/geo/sources", draft))
}

export function useChangeGeoSource() {
  return useRefreshing(({ id, draft }: { id: number; draft: GeoSourceDraft }) =>
    client.put(`/geo/sources/${id}`, draft),
  )
}

export function useRemoveGeoSource() {
  return useRefreshing((id: number) => client.delete(`/geo/sources/${id}`))
}

export function useMoveGeoSource() {
  return useRefreshing(({ id, up }: { id: number; up: boolean }) => client.post(`/geo/sources/${id}/move`, { up }))
}

export function useUpdateGeoSource() {
  return useRefreshing((id: number) => client.post(`/geo/sources/${id}/update`))
}

export function useUpdateGeo() {
  return useRefreshing(() => client.post("/geo/update"))
}

export function draftOf(source: GeoSource): GeoSourceDraft {
  return {
    name: source.name,
    kind: source.kind,
    url: source.url,
    isEnabled: source.isEnabled,
  }
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["geo"] })
    },
  })
}
