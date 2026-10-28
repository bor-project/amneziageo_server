import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import axios from "axios"
import type { AxiosInstance } from "axios"
import type { TextKey } from "@/i18n/en"
import { client } from "./client"

// Where the releases are reached: through the API of the panel, or at a service of the releases itself.
export interface ReleaseRoad {
  http: AxiosInstance
  base: string
  home: string
}

let road: ReleaseRoad = { http: client, base: "/client-updates", home: "/settings/client-updates" }

export function takeReleaseRoad(next: ReleaseRoad): void {
  road = next
}

// The address of a page of the releases, the list of them without a page named.
export function releasePage(rest = ""): string {
  const home = road.home.replace(/\/+$/, "")

  if (rest.length === 0) {
    return home.length === 0 ? "/" : home
  }

  return `${home}/${rest}`
}

export const own = "own"
export const address = "address"

export type ReleaseChannel = "stable" | "beta" | "test"

export const channels: ReleaseChannel[] = ["stable", "beta", "test"]

export const channelWords: Record<string, TextKey> = {
  stable: "channel.stable",
  beta: "channel.beta",
  test: "channel.test",
}

export const releasesPerPage = 20

export interface ClientUpdateDraft {
  isEnabled: boolean
  source: string
  address: string
  token: string
  directory: string
  port: number
  path: string
  default: boolean
}

export interface StoreStatus {
  directory: string
  writable: boolean
  freeBytes: number
  totalBytes: number
  releases: number
  bytes: number
  fault: string
}

export interface ClientUpdates extends ClientUpdateDraft {
  fault: string
  detail: string
  addressFault: string
  pin: string
  checked: string | null
  status: StoreStatus | null
  panelPort: number
  defaultDirectory: string
  url: string
}

export interface ReleaseSummary {
  tag: string
  version: string
  channel: ReleaseChannel
  description: string
  created: string
  updated: string
  files: number
  bytes: number
}

export interface ReleaseFile {
  name: string
  size: number
  sha256: string
  from: string
}

export interface ReleaseDetails {
  release: ReleaseSummary
  files: ReleaseFile[]
  missing: string[]
}

export interface ReleasePage {
  items: ReleaseSummary[]
  page: number
  size: number
  total: number
}

// Why the file of a refusal is not the file of the release.
export interface FileFault {
  name: string
  reason: string
  found: string
  wanted: string
}

// One file a release carries: its name and, for a patch, the installer it goes with.
export interface ReleaseSlot {
  name: string
  installer: string | null
}

export function useClientUpdates(enabled = true) {
  return useQuery({
    queryKey: ["client-updates"],
    queryFn: async () => (await client.get<ClientUpdates>("/client-updates")).data,
    enabled,
  })
}

export function useSaveClientUpdates() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (draft: ClientUpdateDraft) => (await client.put<ClientUpdates>("/client-updates", sent(draft))).data,
    onSuccess: async (saved) => {
      queryClient.setQueryData(["client-updates"], saved)
      await queryClient.invalidateQueries({ queryKey: ["client-releases"] })
      await queryClient.invalidateQueries({ queryKey: ["services"] })
    },
  })
}

export function useCheckClientUpdates() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async () => (await client.post<ClientUpdates>("/client-updates/check", {}, { timeout: 30000 })).data,
    onSuccess: (checked) => queryClient.setQueryData(["client-updates"], checked),
  })
}

export function useClientReleases(page: number, enabled = true) {
  return useQuery({
    queryKey: ["client-releases", "page", page],
    queryFn: async () =>
      (await road.http.get<ReleasePage>(`${road.base}/releases`, { params: { page, size: releasesPerPage } })).data,
    placeholderData: keepPreviousData,
    enabled,
  })
}

export function useClientRelease(tag: string | undefined) {
  return useQuery({
    queryKey: ["client-releases", "tag", tag],
    queryFn: async () => (await road.http.get<ReleaseDetails>(`${road.base}/releases/${tag}`)).data,
    enabled: tag !== undefined,
  })
}

export function useRemoveClientRelease() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (tag: string) => road.http.delete(`${road.base}/releases/${tag}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["client-releases"] }),
  })
}

export async function createRelease(version: string, channel: ReleaseChannel, description: string): Promise<ReleaseDetails> {
  return (await road.http.post<ReleaseDetails>(`${road.base}/releases`, { version, channel, description })).data
}

export async function changeRelease(
  tag: string,
  change: { channel?: ReleaseChannel; description?: string; from?: Record<string, string> },
): Promise<ReleaseDetails> {
  return (await road.http.patch<ReleaseDetails>(`${road.base}/releases/${tag}`, change)).data
}

export async function dropReleaseFile(tag: string, name: string): Promise<ReleaseDetails> {
  return (await road.http.delete<ReleaseDetails>(`${road.base}/releases/${tag}/files/${encodeURIComponent(name)}`)).data
}

// Hands a file of a release to the panel, which checks it and puts it beside the manifest.
export async function putReleaseFile(
  tag: string,
  name: string,
  file: File,
  from: string,
  progress: (share: number) => void,
): Promise<ReleaseDetails> {
  return (
    await road.http.put<ReleaseDetails>(`${road.base}/releases/${tag}/files/${encodeURIComponent(name)}`, file, {
      params: from.length > 0 ? { from } : {},
      headers: { "Content-Type": "application/octet-stream" },
      timeout: 0,
      onUploadProgress: (event) => progress(event.total ? event.loaded / event.total : 0),
    })
  ).data
}

// Hands a zip of a release to the panel, which checks every file in it and takes them all or none.
export async function putReleaseArchive(tag: string, file: File, progress: (share: number) => void): Promise<ReleaseDetails> {
  return (
    await road.http.post<ReleaseDetails>(`${road.base}/releases/${tag}/archive`, file, {
      headers: { "Content-Type": "application/zip" },
      timeout: 0,
      onUploadProgress: (event) => progress(event.total ? event.loaded / event.total : 0),
    })
  ).data
}

// The fault of a file a refusal names, null for a refusal of something else.
export function fileFault(error: unknown): FileFault | null {
  if (!axios.isAxiosError(error)) {
    return null
  }

  const file = (error.response?.data as { file?: FileFault | null } | undefined)?.file

  return file ?? null
}

export function draftOf(one: ClientUpdates): ClientUpdateDraft {
  return {
    isEnabled: one.isEnabled,
    source: one.source,
    address: one.address,
    token: one.token,
    directory: one.directory,
    port: one.port,
    path: one.path,
    default: one.default,
  }
}

export function isVersion(text: string): boolean {
  return /^\d{1,9}\.\d{1,9}\.\d{1,9}\.\d{1,9}$/.test(text)
}

// The files a release of a version is made of, in the order the panel lists them.
export function slotsOf(version: string): ReleaseSlot[] {
  const slots: ReleaseSlot[] = []
  const patched = (name: string) => {
    slots.push({ name, installer: null })
    slots.push({ name: name.replace(/\.(exe|deb)$/, ".patch"), installer: name })
  }

  for (const arch of ["x64", "arm64"]) {
    patched(`AmneziaGeo-${version}-win-${arch}.exe`)
    patched(`AmneziaGeo-${version}-win-${arch}-fdd.exe`)
  }

  for (const name of ["amneziageo", "amneziageo-gui"]) {
    for (const arch of ["amd64", "arm64"]) {
      patched(`${name}_${version}_${arch}.deb`)
    }
  }

  for (const tail of ["", "-arm64", "-arm", "-x64"]) {
    slots.push({ name: `AmneziaGeo-${version}-android${tail}.apk`, installer: null })
  }

  return slots
}

// The version and the channel a tag of a release stands for, as the list shows it.
export function shownTag(tag: string): string {
  return tag.startsWith("v") ? tag.slice(1) : tag
}

// A new token of the store of the panel.
export function freshToken(): string {
  return Array.from(crypto.getRandomValues(new Uint8Array(24)), (one) => one.toString(16).padStart(2, "0")).join("")
}

// What goes to the panel: a token left empty for a service apart keeps the one the panel holds.
function sent(draft: ClientUpdateDraft) {
  return { ...draft, token: draft.source === address && draft.token.length === 0 ? null : draft.token }
}
