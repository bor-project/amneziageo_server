import { Navigate, Outlet, useNavigate, useOutletContext, useParams, useSearchParams } from "react-router-dom"
import { draftOf, useAddClient, useChangeClient, useClientDraft, useClients } from "@/api/clients"
import type { Client } from "@/api/clients"
import { scopes } from "@/api/scopes"
import { ClientConfig } from "@/components/ClientConfig"
import { ClientForm } from "@/components/ClientForm"
import { Tabs } from "@/components/Tabs"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"
import { useSpot } from "@/store/spots"

export function NewClient() {
  const t = useText()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const draft = useClientDraft()
  const add = useAddClient()
  const asked = Number(params.get("config") ?? 0)
  const back = useSpot("/clients")

  useTail([{ label: t("clients.newTitle") }])

  if (draft.data === undefined) {
    return <div className="mt-4 text-sm text-muted">{t("clients.busy")}</div>
  }

  const start = draftOf(draft.data)

  return (
    <ClientForm
      start={{ ...start, name: "", configId: asked > 0 ? asked : start.configId, address: [] }}
      pending={add.isPending}
      error={add.error}
      onSave={(body) => void add.mutateAsync(body).then((made) => navigate(`/clients/${made.id}/export`))}
      onClose={() => navigate(back)}
    />
  )
}

// A client with its two tabs: what it is handed out as, and its settings.
export function ClientView() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const { clientId } = useParams()
  const clients = useClients()
  const back = useSpot("/clients")
  const held = (clients.data ?? []).find((one) => one.id === Number(clientId))

  useTail(held === undefined ? [] : [{ label: held.name }])

  if (held === undefined) {
    return clients.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("clients.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  const tabs = [{ to: `/clients/${held.id}/export`, label: t("tab.export") }]
  if (holds(user, scopes.manageClients)) {
    tabs.push({ to: `/clients/${held.id}/settings`, label: t("tab.settings") })
  }

  return (
    <>
      <Tabs items={tabs} />
      <Outlet context={held} />
    </>
  )
}

export function ClientExport() {
  const user = useAppSelector((s) => s.auth.user)
  const held = useOutletContext<Client>()

  return (
    <div className="mt-4">
      <ClientConfig id={held.id} editable={holds(user, scopes.manageClients)} />
    </div>
  )
}

export function ClientSettings() {
  const navigate = useNavigate()
  const held = useOutletContext<Client>()
  const change = useChangeClient()
  const back = useSpot("/clients")

  return (
    <ClientForm
      key={held.id}
      start={draftOf(held)}
      self={held.id}
      pending={change.isPending}
      error={change.error}
      onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(back))}
      onClose={() => navigate(back)}
      onRemove={() => navigate(`/clients/${held.id}/delete`)}
    />
  )
}
