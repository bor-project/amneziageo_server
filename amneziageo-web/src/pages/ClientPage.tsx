import { Navigate, useNavigate, useParams, useSearchParams } from "react-router-dom"
import { draftOf, useAddClient, useChangeClient, useClientDraft, useClients } from "@/api/clients"
import { ClientForm } from "@/components/ClientForm"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { clientTrail } from "@/pages/trails"

export function ClientPage() {
  const { clientId } = useParams()

  return clientId === undefined ? <NewClient /> : <HeldClient clientId={Number(clientId)} />
}

function NewClient() {
  const t = useText()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const draft = useClientDraft()
  const add = useAddClient()
  const configId = Number(params.get("config") ?? 0)

  useTail([{ label: t("clients.newTitle") }])

  if (draft.data === undefined) {
    return <div className="mt-4 text-sm text-muted">{t("clients.busy")}</div>
  }

  return (
    <ClientForm
      start={{ ...draftOf(draft.data), configId, address: [] }}
      pending={add.isPending}
      error={add.error}
      onSave={(body) => void add.mutateAsync(body).then((made) => navigate(`/connections/clients/${made.id}`))}
      onClose={() => navigate("/connections/clients")}
    />
  )
}

function HeldClient({ clientId }: { clientId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const clients = useClients()
  const change = useChangeClient()
  const all = clients.data ?? []
  const held = all.find((one) => one.id === clientId)

  useTail(held === undefined ? [] : [clientTrail(held, all), { label: t("clients.edit") }])

  if (held === undefined) {
    return clients.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("clients.loading")}</div>
    ) : (
      <Navigate to="/connections/clients" replace />
    )
  }

  const card = `/connections/clients/${held.id}`

  return (
    <ClientForm
      start={draftOf(held)}
      self={held.id}
      pending={change.isPending}
      error={change.error}
      onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(card))}
      onClose={() => navigate(card)}
    />
  )
}
