import { Link, Navigate, useNavigate, useParams, useSearchParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useClients } from "@/api/clients"
import type { Client } from "@/api/clients"
import { useApplyConfig, useConfigs } from "@/api/configs"
import type { Config, Obfuscation } from "@/api/configs"
import { scopes } from "@/api/scopes"
import { Handshake, Traffic } from "@/components/ClientStats"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { useTail } from "@/components/crumbs"
import { Box } from "@/components/fields"
import { card, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { configTrail } from "@/pages/trails"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

const parts = ["params", "clients"] as const

type Part = (typeof parts)[number]

const tab = "-mb-px shrink-0 border-b-2 px-3.5 py-2.5 text-sm"

const captions: Record<Part, TextKey> = {
  params: "configs.tabParams",
  clients: "configs.tabClients",
}

const inbounds: Record<string, TextKey> = {
  endpoint: "clients.inboundEndpoint",
  off: "clients.inboundOff",
  server: "clients.inboundServer",
  network: "clients.inboundNetwork",
}

export function ConfigCard() {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const { configId } = useParams()
  const [params, setParams] = useSearchParams()
  const configs = useConfigs()
  const clients = useClients()
  const apply = useApplyConfig()
  const may = holds(user, scopes.manageInterfaces)
  const all = configs.data ?? []
  const held = all.find((one) => one.id === Number(configId))
  const part = shown(params.get("tab"))

  useTail(held === undefined ? [] : [configTrail(held, all), ...(part === "params" ? [] : [{ label: t(captions[part]) }])])

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
    return configs.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("configs.loading")}</div>
    ) : (
      <Navigate to="/connections/interfaces" replace />
    )
  }

  const mine = (clients.data ?? []).filter((one) => one.configId === held.id)

  return (
    <div className="mt-4 flex flex-col gap-5">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <h2 className="truncate text-2xl leading-10 font-semibold tracking-[-0.02em]">{held.name}</h2>
            {held.isEnabled ? (
              <span className="rounded-full bg-good-soft px-2.5 py-1 text-xs text-good">{t("configs.running")}</span>
            ) : (
              <span className="rounded-full bg-chip px-2.5 py-1 text-xs text-muted">{t("configs.stopped")}</span>
            )}
          </div>
          <div className="text-[13px] text-muted">{meta(held)}</div>
        </div>

        {may && (
          <div className="flex shrink-0 items-center gap-2">
            <button
              type="button"
              onClick={() => void apply.mutateAsync(held.id)}
              disabled={apply.isPending}
              className={secondary}
            >
              {t("configs.apply")}
            </button>
            <Link to="edit" className={`flex h-10 items-center ${primary}`}>
              {t("configs.edit")}
            </Link>
            <RowActions
              title={t("configs.actions")}
              actions={[
                {
                  label: t("configs.remove"),
                  onPick: () => navigate(`/connections/interfaces/${held.id}/delete`),
                  alarming: true,
                },
              ]}
            />
          </div>
        )}
      </div>

      {apply.error !== null && <div className="text-sm text-alarm">{t(complaint(apply.error))}</div>}

      <div className="flex gap-4 overflow-x-auto overflow-y-hidden border-b border-line [scrollbar-width:none] sm:gap-6">
        {parts.map((one) => (
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
          <Box caption={t("configs.host")}>
            <div className="text-[15px] text-body">{held.host.length > 0 ? held.host : t("clients.dash")}</div>
          </Box>

          <Box caption={t("configs.port")}>
            <div className="text-[15px] text-body">{held.listenPort}</div>
          </Box>

          <Box caption={t("configs.address")}>
            <div className="text-[15px] text-body">{held.address.join(", ")}</div>
          </Box>

          <Box caption={t("configs.mtu")}>
            <div className="text-[15px] text-body">{held.mtu}</div>
          </Box>

          <Box caption={t("configs.allowed")}>
            <div className="text-[15px] text-body">{listed(held.allowedIps)}</div>
          </Box>

          <Box caption={t("configs.dns")}>
            <div className="text-[15px] text-body">{listed(held.dns)}</div>
          </Box>

          <Box caption={t("configs.keepalive")}>
            <div className="text-[15px] text-body">{held.keepalive}</div>
          </Box>

          <Box caption={t("configs.offlineAfter")}>
            <div className="text-[15px] text-body">{held.offlineAfter}</div>
          </Box>

          <Box caption={t("configs.nat")}>
            <div className="text-[15px] text-body">{t(held.nat ? "action.yes" : "action.no")}</div>
          </Box>

          <Box caption={t("configs.inbound")}>
            <div className="text-[15px] text-body">{t(inbounds[held.inbound] ?? "clients.inboundOff")}</div>
          </Box>

          <Box caption={t("configs.blocked")}>
            <div className="text-[15px] text-body">{listed(held.blocked)}</div>
          </Box>

          <Box caption={t("configs.count")}>
            <div className="text-[15px] text-body">{mine.length}</div>
          </Box>

          <Box caption={t("configs.public")}>
            <div className="font-mono text-xs break-all text-mono">{held.publicKey}</div>
          </Box>

          <Box caption={t("configs.obfuscation")}>
            <div className="font-mono text-xs leading-6 break-all text-mono">{cover(held.obfuscation)}</div>
          </Box>
        </div>
      )}

      {part === "clients" && (
        <div className={card}>
          {mine.length === 0 ? (
            <div className="px-4 py-6 text-sm text-muted">{t("configs.noClients")}</div>
          ) : (
            <Rows
              name="own"
              items={mine}
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
                  sort: (one: Client) => one.state.used,
                  body: "whitespace-nowrap",
                  cell: (one: Client) => <Traffic one={one} />,
                },
                {
                  key: "state",
                  caption: t("clients.state"),
                  sort: (one: Client) => (one.state.lastHandshake === null ? null : Date.parse(one.state.lastHandshake)),
                  cell: (one: Client) => <Handshake one={one} />,
                },
              ]}
            />
          )}
        </div>
      )}
    </div>
  )
}

function meta(held: Config): string {
  const point = held.host.length > 0 ? `${held.host}:${held.listenPort}` : String(held.listenPort)

  return `${point} · ${held.address.join(", ")}`
}

function listed(values: string[]): string {
  return values.length > 0 ? values.join(", ") : "-"
}

function cover(one: Obfuscation): string {
  const numbers: [string, number][] = [
    ["Jc", one.jc],
    ["Jmin", one.jmin],
    ["Jmax", one.jmax],
    ["S1", one.s1],
    ["S2", one.s2],
    ["S3", one.s3],
    ["S4", one.s4],
  ]
  const words: [string, string | null][] = [
    ["H1", one.h1],
    ["H2", one.h2],
    ["H3", one.h3],
    ["H4", one.h4],
    ["I1", one.i1],
    ["I2", one.i2],
    ["I3", one.i3],
    ["I4", one.i4],
    ["I5", one.i5],
  ]

  return [
    ...numbers.filter(([, value]) => value > 0).map(([name, value]) => `${name}=${value}`),
    ...words.filter(([, value]) => value !== null && value.length > 0).map(([name, value]) => `${name}=${value}`),
  ].join(" ")
}

function shown(value: string | null): Part {
  return parts.find((one) => one === value) ?? "params"
}
