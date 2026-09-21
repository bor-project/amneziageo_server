import { Link, Navigate, useNavigate, useParams, useSearchParams } from "react-router-dom"
import { draftOf, useAddClient, useAddDevice, useChangeClient, useClientDraft, useClients } from "@/api/clients"
import type { Client } from "@/api/clients"
import { scopes } from "@/api/scopes"
import { ClientForm } from "@/components/ClientForm"
import { Handshake, Traffic } from "@/components/ClientStats"
import { Rows } from "@/components/Rows"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"
import { lastSpot } from "@/store/spots"

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
  const back = lastSpot("connections", "/connections/clients")

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
  const user = useAppSelector((s) => s.auth.user)
  const clients = useClients()
  const change = useChangeClient()
  const addDevice = useAddDevice()
  const may = holds(user, scopes.manageClients)
  const all = clients.data ?? []
  const held = all.find((one) => one.id === clientId)
  const back = lastSpot("connections", "/connections/clients")

  useTail(held === undefined ? [] : [{ label: held.name }, { label: t("action.settings") }])

  if (held === undefined) {
    return clients.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("clients.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  if (held.parentId !== null) {
    return <Navigate to={`/connections/clients/${held.id}/export`} replace />
  }

  const devices = all.filter((one) => one.parentId === held.id)

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

      {(held.multiDevice || devices.length > 0) && (
        <div className={card}>
          <div className="flex items-center justify-between gap-4 border-b border-line px-4 py-3">
            <div className="text-sm text-ink">{t("clients.tabDevices")}</div>
            {held.multiDevice && (
              <button
                type="button"
                onClick={() =>
                  void addDevice
                    .mutateAsync(held.id)
                    .then((made) => navigate(`/connections/clients/${made.id}/export`))
                }
                disabled={addDevice.isPending}
                className={secondary}
              >
                {t("clients.addDevice")}
              </button>
            )}
          </div>

          {devices.length === 0 ? (
            <div className="px-4 py-6 text-sm text-muted">{t("clients.noDevices")}</div>
          ) : (
            <Rows
              name="device"
              items={devices}
              keyOf={(one) => one.id}
              columns={[
                {
                  key: "name",
                  caption: t("clients.name"),
                  sort: (one: Client) => one.name,
                  lead: true,
                  body: "font-semibold text-ink",
                  cell: (one: Client) => (
                    <Link to={`/connections/clients/${one.id}/export`} className="hover:text-brand-ink">
                      {one.name}
                    </Link>
                  ),
                },
                {
                  key: "address",
                  caption: t("clients.address"),
                  sort: (one: Client) => one.address.join(", "),
                  cell: (one: Client) => one.address.join(", "),
                },
                {
                  key: "traffic",
                  caption: t("clients.traffic"),
                  sort: (one: Client) => one.state.todayRx + one.state.todayTx,
                  body: "whitespace-nowrap",
                  cell: (one: Client) => <Traffic one={one} />,
                },
                {
                  key: "state",
                  caption: t("clients.state"),
                  sort: (one: Client) => (one.state.lastHandshake === null ? null : Date.parse(one.state.lastHandshake)),
                  cell: (one: Client) => <Handshake one={one} />,
                },
                {
                  key: "actions",
                  caption: t("clients.actions"),
                  tail: true,
                  cell: (one: Client) =>
                    may && (
                      <div className="flex justify-end">
                        <button
                          type="button"
                          onClick={() => navigate(`/connections/clients/${one.id}/delete`)}
                          className={`text-sm ${danger}`}
                        >
                          {t("clients.remove")}
                        </button>
                      </div>
                    ),
                },
              ]}
            />
          )}
        </div>
      )}
    </div>
  )
}
