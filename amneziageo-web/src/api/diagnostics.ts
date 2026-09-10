import { useQuery } from "@tanstack/react-query"
import { client } from "./client"

export interface Versions {
  runtime: string
  distribution: string
  kernel: string
  wstunnel: string
}

export interface JournalEntry {
  time: string
  level: string
  category: string
  message: string
  fault: string
}

export function useVersions() {
  return useQuery({
    queryKey: ["diagnostics"],
    queryFn: async () => (await client.get<Versions>("/diagnostics")).data,
  })
}

export function useJournal() {
  return useQuery({
    queryKey: ["diagnostics", "log"],
    queryFn: async () => (await client.get<JournalEntry[]>("/diagnostics/log")).data,
  })
}
