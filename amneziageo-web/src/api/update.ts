import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface UpdateLatest {
  version: string
  published: string | null
  notes: string
}

export interface UpdateRun {
  from: string
  to: string
  state: "running" | "done" | "failed"
  started: string
  log: string[]
}

export interface UpdateState {
  current: string
  channel: string
  mode: string
  blocker: string
  checked: string | null
  latest: UpdateLatest | null
  stage: "idle" | "checking" | "downloading" | "starting"
  fault: { error: string; message: string } | null
  run: UpdateRun | null
}

export function busy(state: UpdateState | undefined): boolean {
  return (
    state !== undefined &&
    (state.stage === "downloading" || state.stage === "starting" || state.run?.state === "running")
  )
}

export function useUpdate(enabled = true) {
  return useQuery({
    queryKey: ["update"],
    enabled,
    queryFn: async () => (await client.get<UpdateState>("/update", { timeout: 5000 })).data,
    refetchInterval: (query) => (busy(query.state.data) ? 3000 : 60000),
  })
}

export function useCheckUpdate() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async () => (await client.post<UpdateState>("/update/check", null, { timeout: 90000 })).data,
    onSuccess: (state) => {
      queryClient.setQueryData(["update"], state)
    },
  })
}

export function useApplyUpdate() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (version: string) => (await client.post<UpdateState>("/update/apply", { version })).data,
    onSuccess: (state) => {
      queryClient.setQueryData(["update"], state)
    },
  })
}
