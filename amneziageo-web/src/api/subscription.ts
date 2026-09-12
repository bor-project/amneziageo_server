import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface SubscriptionDraft {
  isEnabled: boolean
  listen: string[]
  domains: string[]
  port: number
  opened: boolean
  path: string
  certificate: string
  certificateKey: string
  updateHours: number
  title: string
}

export interface Subscription extends SubscriptionDraft {
  certificates: string[]
  addresses: string[]
  certificateRoot: string
  fault: string
}

export function useSubscription() {
  return useQuery({
    queryKey: ["subscription"],
    queryFn: async () => (await client.get<Subscription>("/subscription")).data,
  })
}

export function useSaveSubscription() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (draft: SubscriptionDraft) => client.put("/subscription", draft),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["subscription"] })
      await queryClient.invalidateQueries({ queryKey: ["client-config"] })
    },
  })
}

export function draftOf(one: Subscription): SubscriptionDraft {
  return {
    isEnabled: one.isEnabled,
    listen: one.listen,
    domains: one.domains,
    port: one.port,
    opened: one.opened,
    path: one.path,
    certificate: one.certificate,
    certificateKey: one.certificateKey,
    updateHours: one.updateHours,
    title: one.title,
  }
}
