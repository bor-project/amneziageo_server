import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { useClients } from "@/api/clients"
import type { Client } from "@/api/clients"
import { useConfigs } from "@/api/configs"
import { scopes } from "@/api/scopes"
import { useTemplates } from "@/api/templates"
import { Handshake, Speed, Traffic } from "@/components/ClientStats"
import { RowActions } from "@/components/RowActions"
import type { RowAction } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, fieldBox } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Clients() {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const [params, setParams] = useSearchParams()
  const configs = useConfigs()
  const clients = useClients()
  const templates = useTemplates()
  const may = holds(user, scopes.manageClients)
  const picked = Number(params.get("config") ?? 0)
  const find = params.get("find") ?? ""
  const all = clients.data ?? []
  const shown = all.filter((one) => (picked === 0 || one.configId === picked) && matches(one, find))
  const names = new Map((templates.data ?? []).map((one): [number, string] => [one.id, one.name]))

  function put(key: string, value: string) {
    const kept = new URLSearchParams(params)

    if (value === "") {
      kept.delete(key)
    } else {
      kept.set(key, value)
    }

    setParams(kept, { replace: true })
  }

  function named(one: Client): string {
    return (one.templateId === null ? undefined : names.get(one.templateId)) ?? t("clients.dash")
  }

  function actionsOf(one: Client): RowAction[] {
    const actions: RowAction[] = [
      { label: t("action.export"), onPick: () => navigate(`/connections/clients/${one.id}/export`) },
    ]

    if (one.parentId === null) {
      actions.push({ label: t("action.settings"), onPick: () => navigate(`/connections/clients/${one.id}/edit`) })
    }

    return actions
  }

  return (
    <div className={`mt-4 ${card}`}>
      {shown.length === 0 && all.length === 0 && (
        <div className="px-4 py-6 text-sm text-muted">{t("clients.empty")}</div>
      )}

      {all.length > 0 && (
        <Rows
          name="client"
          items={shown}
          arrange={ordered}
          keyOf={(one) => one.id}
          tools={
            <div className="flex flex-wrap items-center gap-2">
              <select
                id="client-config"
                value={picked}
                onChange={(e) => put("config", e.target.value === "0" ? "" : e.target.value)}
                className={`w-full wide:w-60 ${fieldBox}`}
              >
                <option value={0}>{t("clients.everyConfig")}</option>
                {(configs.data ?? []).map((one) => (
                  <option key={one.id} value={one.id}>
                    {one.name}
                  </option>
                ))}
              </select>

              <input
                value={find}
                placeholder={t("action.search")}
                onChange={(e) => put("find", e.target.value)}
                className={`w-full wide:w-60 ${fieldBox}`}
              />
            </div>
          }
          columns={[
            {
              key: "name",
              caption: t("clients.name"),
              sort: (one) => one.name,
              lead: true,
              body: "font-semibold text-ink",
              cell: (one) => (
                <span className={one.parentId === null ? "" : "pl-6"}>
                  <Link to={`/connections/clients/${one.id}/export`} className="hover:text-brand-ink">
                    {one.name}
                  </Link>
                  {one.state.isOnline && <span className="ml-2 text-xs text-good">{t("clients.online")}</span>}
                  {!one.isEnabled && <span className="ml-2 text-xs text-muted">{t("clients.off")}</span>}
                </span>
              ),
            },
            {
              key: "config",
              caption: t("clients.endpointName"),
              sort: (one) => one.config,
              cell: (one) => one.config,
            },
            {
              key: "template",
              caption: t("clients.template"),
              sort: (one) => named(one),
              cell: (one) =>
                one.templateId === null ? (
                  t("clients.dash")
                ) : (
                  <Link to={`/connections/templates/${one.templateId}/edit`} className="text-brand-ink hover:text-brand-lit">
                    {named(one)}
                  </Link>
                ),
            },
            {
              key: "address",
              caption: t("clients.address"),
              sort: (one) => one.address.join(", "),
              cell: (one) => one.address.join(", "),
            },
            {
              key: "speed",
              caption: t("clients.speed"),
              sort: (one) => one.state.txRate + one.state.rxRate,
              cell: (one) => <Speed one={one} />,
            },
            {
              key: "traffic",
              caption: t("clients.traffic"),
              sort: (one) => (one.parentId === null ? one.state.used : one.state.todayRx + one.state.todayTx),
              body: "whitespace-nowrap",
              cell: (one) => <Traffic one={one} />,
            },
            {
              key: "state",
              caption: t("clients.state"),
              sort: (one) =>
                (one.isEnabled && one.state.isSpent) || !one.state.isPresent || one.state.lastHandshake === null
                  ? null
                  : one.state.isOnline
                    ? Number.MAX_SAFE_INTEGER
                    : Date.parse(one.state.lastHandshake),
              cell: (one) => <Handshake one={one} />,
            },
            {
              key: "actions",
              caption: t("clients.actions"),
              tail: true,
              cell: (one) => may && <RowActions title={t("clients.actions")} actions={actionsOf(one)} />,
            },
          ]}
        />
      )}
    </div>
  )
}

function matches(one: Client, find: string): boolean {
  const query = find.trim().toLowerCase()

  return (
    query === "" ||
    one.name.toLowerCase().includes(query) ||
    one.address.some((address) => address.toLowerCase().includes(query)) ||
    one.note.toLowerCase().includes(query)
  )
}

function ordered(clients: Client[]): Client[] {
  const tops = clients.filter((one) => one.parentId === null)
  const known = new Set(tops.map((one) => one.id))
  const rows = tops.flatMap((top) => [top, ...clients.filter((one) => one.parentId === top.id)])

  return [...rows, ...clients.filter((one) => one.parentId !== null && !known.has(one.parentId))]
}
