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
  return useMutation({ mutationFn: () => client.post("/panel/restart") })
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

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["panel"] })
    },
  })
}
