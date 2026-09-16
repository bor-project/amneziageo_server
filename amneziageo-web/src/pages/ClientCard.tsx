import { Link, Navigate, useNavigate, useParams, useSearchParams } from "react-router-dom"
import { useAddDevice, useClients, useSwitchClient } from "@/api/clients"
import type { Client, Inbound } from "@/api/clients"
import { scopes } from "@/api/scopes"
import { useTemplates } from "@/api/templates"
import { ClientConfig } from "@/components/ClientConfig"
import { Handshake, Speed, Traffic } from "@/components/ClientStats"
import { RowActions } from "@/components/RowActions"
import type { RowAction } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { useTail } from "@/components/crumbs"
import { Box } from "@/components/fields"
import { card, primary, secondary } from "@/components/styles"
import { bytes } from "@/format"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { clientTrail } from "@/pages/trails"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

const parts = ["params", "config", "devices"] as const

type Part = (typeof parts)[number]

const tab = "-mb-px shrink-0 border-b-2 px-3.5 py-2.5 text-sm"

const captions: Record<Part, TextKey> = {
  params: "clients.tabParams",
  config: "clients.tabConfig",
  devices: "clients.tabDevices",
}

const inbounds: Record<Inbound, TextKey> = {
  endpoint: "clients.inboundEndpoint",
  off: "clients.inboundOff",
  server: "clients.inboundServer",
  network: "clients.inboundNetwork",
}

export function ClientCard() {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const { clientId } = useParams()
  const [params, setParams] = useSearchParams()
  const clients = useClients()
  const templates = useTemplates()
  const turn = useSwitchClient()
  const addDevice = useAddDevice()
  const may = holds(user, scopes.manageClients)
  const all = clients.data ?? []
  const held = all.find((one) => one.id === Number(clientId))
  const part = shown(params.get("tab"))

  useTail(held === undefined ? [] : [clientTrail(held, all), ...(part === "params" ? [] : [{ label: t(captions[part]) }])])

  function open(next: Part) {
    const kept = new URLSearchParams(params)

    if (next === "params") {
      kept.delete("tab")
    } else {
      kept.set("tab", next)
    }

    setParams(kept, { replace: true })
  }

  if (held === undefined) {
    return clients.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("clients.loading")}</div>
    ) : (
      <Navigate to="/connections/clients" replace />
    )
  }

  const devices = all.filter((one) => one.parentId === held.id)
  const template = (templates.data ?? []).find((one) => one.id === held.templateId)

  function actionsOf(one: Client): RowAction[] {
    const more: RowAction[] =
      one.parentId === null && one.multiDevice
        ? [
            {
              label: t("clients.addDevice"),
              onPick: () =>
                void addDevice.mutateAsync(one.id).then((made) => navigate(`/connections/clients/${made.id}?tab=config`)),
            },
          ]
        : []

    return [
      ...more,
      { label: t("clients.remove"), onPick: () => navigate(`/connections/clients/${one.id}/delete`), alarming: true },
    ]
  }

  return (
    <div className="mt-4 flex flex-col gap-5">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <h2 className="truncate text-2xl leading-10 font-semibold tracking-[-0.02em]">{held.name}</h2>
            <Mark one={held} t={t} />
          </div>
          <div className="text-[13px] text-muted">{`${held.config} · ${held.address.join(", ")}`}</div>
        </div>

        {may && (
          <div className="flex shrink-0 items-center gap-2">
            <button
              type="button"
              onClick={() => void turn.mutateAsync({ id: held.id, on: !held.isEnabled })}
              disabled={turn.isPending}
              className={secondary}
            >
              {held.isEnabled ? t("clients.turnOff") : t("clients.turnOn")}
            </button>
            {held.parentId === null && (
              <Link to="edit" className={`flex h-10 items-center ${primary}`}>
                {t("clients.edit")}
              </Link>
            )}
            <RowActions title={t("clients.actions")} actions={actionsOf(held)} />
          </div>
        )}
      </div>

      <div className="flex gap-4 overflow-x-auto overflow-y-hidden border-b border-line [scrollbar-width:none] sm:gap-6">
        {parts
          .filter((one) => one !== "devices" || held.multiDevice || devices.length > 0)
          .map((one) => (
            <button
              key={one}
              type="button"
              onClick={() => open(one)}
              className={`${tab} ${one === part ? "border-brand font-medium text-ink" : "border-transparent text-muted hover:text-ink"}`}
            >
              {t(captions[one])}
            </button>
          ))}
      </div>

      {part === "params" && (
        <div className="grid gap-3.5 [grid-template-columns:repeat(auto-fit,minmax(210px,1fr))]">
          <Box caption={t("clients.endpointName")}>
            <div className="text-[15px] text-body">{held.config}</div>
          </Box>

          <Box caption={t("clients.template")}>
            <div className="text-[15px] text-body">
              {template === undefined ? (
                t("clients.noTemplate")
              ) : (
                <Link to={`/connections/templates/${template.id}`} className="text-brand-ink hover:text-brand-lit">
                  {template.name}
                </Link>
              )}
            </div>
          </Box>

          <Box caption={t("clients.address")}>
            <div className="text-[15px] text-body">{held.address.join(", ")}</div>
          </Box>

          <Box caption={t("clients.traffic")}>
            <div className="text-[15px] text-body">
              <Traffic one={held} />
            </div>
          </Box>

          <Box caption={t("clients.speed")}>
            <div className="text-[15px] text-body">
              <Speed one={held} />
            </div>
          </Box>

          <Box caption={t("clients.state")}>
            <div className="text-[15px] text-body">
              <Handshake one={held} />
            </div>
          </Box>

          <Box caption={t("clients.limit")}>
            <div className="text-[15px] text-body">
              {held.dailyLimit > 0 ? bytes(t, held.dailyLimit) : t("clients.noLimit")}
            </div>
          </Box>

          <Box caption={t("clients.subscription")}>
            <div className="text-[15px] break-all text-body">
              {held.subscriptionId.length > 0 ? held.subscriptionId : t("clients.dash")}
            </div>
          </Box>

          <Box caption={t("clients.inbound")}>
            <div className="text-[15px] text-body">{t(inbounds[held.inbound])}</div>
          </Box>

          <Box caption={t("clients.multiDevice")}>
            <div className="text-[15px] text-body">{t(held.multiDevice ? "action.yes" : "action.no")}</div>
          </Box>

          <Box caption={t("clients.routes")}>
            <div className="text-[15px] text-body">
              {held.routes.length > 0 ? held.routes.join(", ") : t("clients.noRoutes")}
            </div>
          </Box>

          <Box caption={t("clients.forwards")}>
            <div className="text-[15px] text-body">
              {held.forwards.length > 0
                ? held.forwards.map((one) => `${one.protocol} ${one.from} ${t("clients.forwardTo")} ${one.to}`).join(", ")
                : t("clients.dash")}
            </div>
          </Box>

          <Box caption={t("clients.note")}>
            <div className="text-[15px] text-body">{held.note.length > 0 ? held.note : t("clients.dash")}</div>
          </Box>

          <Box caption={t("clients.publicKey")}>
            <div className="font-mono text-xs break-all text-mono">{held.publicKey}</div>
          </Box>

          {held.parentId !== null && (
            <Box caption={t("clients.parent")}>
              <div className="text-[15px] text-body">
                <Link to={`/connections/clients/${held.parentId}`} className="text-brand-ink hover:text-brand-lit">
                  {all.find((one) => one.id === held.parentId)?.name ?? t("clients.dash")}
                </Link>
              </div>
            </Box>
          )}
        </div>
      )}

      {part === "config" && <ClientConfig id={held.id} />}

      {part === "devices" && (
        <div className={card}>
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
                    <Link to={`/connections/clients/${one.id}`} className="hover:text-brand-ink">
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
                  sort: (one: Client) =>
                    one.state.lastHandshake === null ? null : Date.parse(one.state.lastHandshake),
                  cell: (one: Client) => <Handshake one={one} />,
                },
                {
                  key: "actions",
                  caption: t("clients.actions"),
                  tail: true,
                  cell: (one: Client) => may && <RowActions title={t("clients.actions")} actions={actionsOf(one)} />,
                },
              ]}
            />
          )}
        </div>
      )}
    </div>
  )
}

function Mark({ one, t }: { one: Client; t: Text }) {
  if (!one.isEnabled) {
    return <span className="rounded-full bg-chip px-2.5 py-1 text-xs text-muted">{t("clients.off")}</span>
  }

  if (one.state.isOnline) {
    return <span className="rounded-full bg-good-soft px-2.5 py-1 text-xs text-good">{t("clients.online")}</span>
  }

  return null
}

function shown(value: string | null): Part {
  return parts.find((one) => one === value) ?? "params"
}
