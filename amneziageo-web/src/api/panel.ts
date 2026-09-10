import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface PanelDraft {
  listen: string[]
  domains: string[]
  port: number
  path: string
  certificate: string
  certificateKey: string
  language: string
}

export interface Panel extends PanelDraft {
  certificates: string[]
  addresses: string[]
  certificateRoot: string
  pending: boolean
}

export function usePanel() {
  return useQuery({
    queryKey: ["panel"],
    queryFn: async () => (await client.get<Panel>("/panel")).data,
  })
}

export function useSavePanel() {
  return useRefreshing((draft: PanelDraft) => client.put("/panel", draft))
}

export function useRestartPanel() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async () => {
      await client.post("/panel/restart")
      await started()
    },
    onSettled: async () => {
      await queryClient.invalidateQueries()
    },
  })
}

export function draftOf(panel: Panel): PanelDraft {
  return {
    listen: panel.listen,
    domains: panel.domains,
    port: panel.port,
    path: panel.path,
    certificate: panel.certificate,
    certificateKey: panel.certificateKey,
    language: panel.language,
  }
}

async function started() {
  const until = Date.now() + 60000
  while (Date.now() < until) {
    await pause(1000)
    const answer = await client.get<Panel>("/panel", { timeout: 3000 }).catch(() => null)
    if (answer !== null && !answer.data.pending) {
      return
    }
  }
}

function pause(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["panel"] })
    },
  })
}
