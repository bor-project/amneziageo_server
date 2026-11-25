import { useLocation, useNavigate } from "react-router-dom"
import type { EntryDraft } from "@/api/routingEntries"

export interface EntryReturn {
  to: string
  draft: EntryDraft
}

export interface RoutingPageState {
  entryResume?: EntryDraft
  entryReturn?: EntryReturn
}


export function useRelatedReturn(back: string) {
  const navigate = useNavigate()
  const returning = (useLocation().state as RoutingPageState | null)?.entryReturn
  return (change: Partial<EntryDraft> = {}) => {
    if (returning) {
      navigate(returning.to, { state: { entryResume: { ...returning.draft, ...change } } satisfies RoutingPageState })
    } else {
      navigate(back)
    }
  }
}
