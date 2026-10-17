import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export type Verdict = "out" | "host" | "block" | "held" | "guard"

export type Grouping = "domain" | "name" | "rule" | "way" | "client"

export type Path = "local" | "relay"

export type Outcome = "ok" | "empty" | "reset" | "silent" | "unreachable"

export interface AccessState {
  isEnabled: boolean
  days: number
  isRunning: boolean
  fault: string
  lost: number
  records: number
  oldest: string | null
  newest: string | null
  bytes: number
  names: boolean
}

export interface AccessRecord {
  id: number
  at: string
  client: string
  source: string
  sourcePort: number
  inbound: string
  protocol: number
  target: string
  port: number
  name: string
  verdict: Verdict
  rule: number | null
  ruleName: string
  way: string
  via: string
  path: Path | ""
  outcome: Outcome | ""
}

export interface AccessCount {
  key: string
  count: number
}

export interface AccessGroup {
  key: string
  verdict: Verdict
  way: string
  via: string
  ruleName: string
  clients: number
  count: number
  ok: number
  failed: number
  first: string
  last: string
}

export interface AccessSummary {
  total: number
  verdicts: AccessCount[]
  ways: AccessCount[]
  paths: AccessCount[]
  outcomes: AccessCount[]
  groups: AccessGroup[]
  isCut: boolean
}

export interface AccessFilter {
  from: string
  to: string
  client: string
  verdict: string
  way: string
  path: string
  outcome: string
  search: string
}

const page = 200

export function useAccess() {
  return useQuery({
    queryKey: ["access"],
    queryFn: async () => (await client.get<AccessState>("/access")).data,
  })
}

export function useSaveAccess() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (change: { isEnabled?: boolean; days?: number }) =>
      (await client.put<AccessState>("/access", change, { timeout: 60000 })).data,
    onSuccess: (state) => {
      queryClient.setQueryData(["access"], state)
    },
  })
}

export function useClearAccess() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async () => {
      await client.delete("/access/records", { timeout: 60000 })
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["access"] })
    },
  })
}

export function useAccessSummary(filter: AccessFilter, by: Grouping) {
  return useQuery({
    queryKey: ["access", "summary", filter, by],
    queryFn: async () =>
      (await client.get<AccessSummary>("/access/summary", { params: { ...given(filter), by }, timeout: 60000 })).data,
    placeholderData: keepPreviousData,
  })
}

export function useAccessRecords(filter: AccessFilter, enabled = true) {
  return useInfiniteQuery({
    queryKey: ["access", "records", filter],
    queryFn: async ({ pageParam }) =>
      (
        await client.get<AccessRecord[]>("/access/records", {
          params: { ...given(filter), limit: page, before: pageParam },
          timeout: 60000,
        })
      ).data,
    initialPageParam: undefined as number | undefined,
    getNextPageParam: (last) => (last.length < page ? undefined : last[last.length - 1].id),
    placeholderData: keepPreviousData,
    enabled,
  })
}

function given(filter: AccessFilter): Partial<AccessFilter> {
  return Object.fromEntries(Object.entries(filter).filter(([, value]) => value !== ""))
}
