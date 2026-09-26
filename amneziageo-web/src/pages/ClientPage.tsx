import { Link, Navigate, useNavigate, useParams, useSearchParams } from "react-router-dom"
import { draftOf, useAddClient, useChangeClient, useClientDraft, useClients } from "@/api/clients"
import { ClientForm } from "@/components/ClientForm"
import { useTail } from "@/components/crumbs"
import { secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

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
  const asked = Number(params.get("config") ?? 0)
  const back = useSpot("/connections/clients")

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
      onSave={(body) => void add.mutateAsync(body).then((made) => navigate(`/connections/clients/${made.id}/export`))}
      onClose={() => navigate(back)}
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
  const back = useSpot("/connections/clients")

  useTail(
    held === undefined
      ? []
      : [{ label: held.name, to: `/connections/clients/${held.id}/export` }, { label: t("action.settings") }],
  )

  if (held === undefined) {
    return clients.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("clients.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <div className="flex justify-end">
        <Link to={`/connections/clients/${held.id}/export`} className={secondary}>
          {t("action.export")}
        </Link>
      </div>

      <ClientForm
        start={draftOf(held)}
        self={held.id}
        pending={change.isPending}
        error={change.error}
        onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(back))}
        onClose={() => navigate(back)}
        onRemove={() => navigate(`/connections/clients/${held.id}/delete`)}
      />
    </div>
  )
}
