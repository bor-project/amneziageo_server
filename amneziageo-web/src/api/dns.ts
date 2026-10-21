import { useEffect, useState } from "react"
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ClientDraft } from "./clients"
import { client } from "./client"
import type { ConfigDraft } from "./configs"
import type { RuleDraft } from "./rules"
import { scopes } from "./scopes"
import type { TemplateDraft } from "./templates"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

// Where the name servers of a file come from: the level above, the resolver of the panel or the servers listed.
export type DnsMode = "auto" | "panel" | "own"

// Where the name servers of a file were picked.
export type DnsFrom = "client" | "template" | "endpoint" | "standins"

export type DnsLevel = "error" | "warning" | "hint"

export type DnsKind = "dns" | "rule" | "basic" | "template" | "config" | "client"

export interface DnsSubject {
  kind: DnsKind
  id: number
  name: string
}

export interface DnsAffected {
  id: number
  name: string
  from: DnsFrom
  source: string
  servers: string[]
}

export interface DnsFinding {
  code: string
  level: DnsLevel
  subjects: DnsSubject[]
  clients: DnsAffected[]
}

export interface DnsNeeds {
  rules: DnsSubject[]
  templates: DnsSubject[]
  picked: DnsAffected[]
  holders: DnsAffected[]
}

export interface DnsReport {
  needs: DnsNeeds
  findings: DnsFinding[]
}

export interface DnsLine {
  servers: string[]
  from: DnsFrom
  choice: DnsMode
  isPanel: boolean
  endpoint: string
  lost: boolean
}

export interface DnsCheck {
  line: DnsLine | null
  endpoint: string
  findings: DnsFinding[]
  lines: DnsLine[]
}

export interface DnsCheckRequest {
  id?: number
  client?: ClientDraft
  template?: TemplateDraft
  config?: ConfigDraft
  rule?: RuleDraft
  dns?: DnsDraft
}

export interface DnsState {
  isRunning: boolean
  listening: string[]
  fault: string
  questions: number
  cached: number
  failed: number
  added: number
  waiting: number
  names: number
  startedUtc: string | null
  keptOut: string[]
  needed: boolean
  holders: number
  picked: number
}

export interface DnsDraft {
  isEnabled: boolean
  port: number
  upstreams: string[]
  outbound: string
  listen: string[]
  nameMinutes: number
  cacheSize: number
  minTtl: number
  maxTtl: number
}

export interface Resolver extends DnsDraft {
  pending: boolean
  state: DnsState
}

export function useResolver(poll = true) {
  return useQuery({
    queryKey: ["dns"],
    queryFn: async () => (await client.get<Resolver>("/dns")).data,
    refetchInterval: poll ? 5000 : false,
  })
}

export function useSaveResolver() {
  return useRefreshing((draft: DnsDraft) => client.put("/dns", draft))
}

export function useRestartResolver() {
  return useRefreshing(() => client.post("/dns/restart"))
}

// Who needs the resolver and what the settings that decide the name servers of the clients contradict.
export function useDnsConflicts() {
  const user = useAppSelector((s) => s.auth.user)

  return useQuery({
    queryKey: ["dns-conflicts"],
    queryFn: async () => (await client.get<DnsReport>("/dns/conflicts")).data,
    enabled: holds(user, scopes.readState),
    refetchInterval: 10000,
    retry: false,
  })
}

// What a draft would contradict and the name servers it gives a file, asked once the draft stops changing.
export function useDnsCheck(request: DnsCheckRequest | null) {
  const user = useAppSelector((s) => s.auth.user)
  const settled = useSettled(request)

  return useQuery({
    queryKey: ["dns-check", settled],
    queryFn: async () => (await client.post<DnsCheck>("/dns/check", settled)).data,
    enabled: settled !== null && holds(user, scopes.readState),
    placeholderData: keepPreviousData,
    staleTime: 5000,
    retry: false,
  })
}

// The findings that name a setting.
export function findingsOf(report: DnsReport | undefined, kind: DnsKind, id: number): DnsFinding[] {
  return (report?.findings ?? []).filter((one) => one.subjects.some((subject) => subject.kind === kind && subject.id === id))
}

// The findings that touch a client.
export function findingsAbout(report: DnsReport | undefined, id: number): DnsFinding[] {
  return (report?.findings ?? []).filter((one) => one.clients.some((affected) => affected.id === id))
}

const settleMs = 400

function useSettled<T>(value: T): T {
  const [held, setHeld] = useState(value)
  const key = JSON.stringify(value)

  useEffect(() => {
    const timer = window.setTimeout(() => setHeld(JSON.parse(key) as T), settleMs)

    return () => window.clearTimeout(timer)
  }, [key])

  return held
}

export function draftOf(resolver: Resolver): DnsDraft {
  return {
    isEnabled: resolver.isEnabled,
    port: resolver.port,
    upstreams: resolver.upstreams,
    outbound: resolver.outbound,
    listen: resolver.listen,
    nameMinutes: resolver.nameMinutes,
    cacheSize: resolver.cacheSize,
    minTtl: resolver.minTtl,
    maxTtl: resolver.maxTtl,
  }
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["dns"] })
      await queryClient.invalidateQueries({ queryKey: ["rules"] })
      await queryClient.invalidateQueries({ queryKey: ["dns-conflicts"] })
    },
  })
}
