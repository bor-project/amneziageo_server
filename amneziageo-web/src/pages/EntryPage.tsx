import { Navigate, useLocation, useNavigate, useParams } from "react-router-dom"
import { entryDraft, freshEntry, useAddEntry, useChangeEntry, useRoutingEntries } from "@/api/routingEntries"
import type { EntryDraft } from "@/api/routingEntries"
import { EntryForm } from "@/components/EntryForm"
import { useTail } from "@/components/crumbs"
import type { RoutingPageState } from "@/components/routingReturn"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function EntryPage() {
  const { entryId } = useParams()
  return entryId === undefined ? <NewEntry /> : <HeldEntry entryId={Number(entryId)} />
}

function useRelatedEditor() {
  const navigate = useNavigate()
  const location = useLocation()

  return (draft: EntryDraft, target: string) => {
    const to = location.pathname + location.search
    navigate(to, { replace: true, state: { entryResume: draft } satisfies RoutingPageState })
    navigate(target, { state: { entryReturn: { to, draft } } satisfies RoutingPageState })
  }
}

function NewEntry() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/rules")
  const add = useAddEntry()
  const openEditor = useRelatedEditor()
  const resumed = (useLocation().state as RoutingPageState | null)?.entryResume
  useTail([{ label: t("routing.newEntry") }])

  return <EntryForm start={freshEntry} resume={resumed} pending={add.isPending} error={add.error} onAddresses={(draft, id) => openEditor(draft, id === undefined ? "/routing/addresses/new" : `/routing/addresses/${id}/edit`)} onHandling={openEditor}
    onSave={(draft) => void add.mutateAsync(draft).then(() => navigate(back)).catch(() => undefined)} onClose={() => navigate(back)} />
}

function HeldEntry({ entryId }: { entryId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/rules")
  const entries = useRoutingEntries()
  const change = useChangeEntry()
  const openEditor = useRelatedEditor()
  const resumed = (useLocation().state as RoutingPageState | null)?.entryResume
  const held = entries.data?.find((one) => one.id === entryId)
  useTail(held === undefined ? [] : [{ label: held.name }])

  if (held === undefined) {
    return entries.data === undefined ? <div className="mt-4 text-sm text-muted">{t("rules.loading")}</div> : <Navigate to={back} replace />
  }

  return <EntryForm key={held.id} start={entryDraft(held)} resume={resumed} pending={change.isPending} error={change.error} onAddresses={(draft, id) => openEditor(draft, id === undefined ? "/routing/addresses/new" : `/routing/addresses/${id}/edit`)} onHandling={openEditor}
    onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(back)).catch(() => undefined)}
    onClose={() => navigate(back)} onRemove={() => navigate(`/routing/rules/${held.id}/delete`)} />
}
