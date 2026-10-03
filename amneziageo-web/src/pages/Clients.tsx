import { useState } from "react"
import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useClients, useSwitchClient, useSwitchClients } from "@/api/clients"
import type { Client } from "@/api/clients"
import { useConfigs } from "@/api/configs"
import { scopes } from "@/api/scopes"
import { useTemplates } from "@/api/templates"
import { Handshake, Speed, Traffic } from "@/components/ClientStats"
import { RowActions } from "@/components/RowActions"
import type { RowAction } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { summary } from "@/components/batch"
import type { Summary } from "@/components/batch"
import { Find, Knob } from "@/components/fields"
import { card, fieldBox, tool, toolRisky } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

const nothing: ReadonlySet<string | number> = new Set()

const quiet: Summary = { text: "", title: "" }

export function Clients() {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const [params, setParams] = useSearchParams()
  const configs = useConfigs()
  const clients = useClients()
  const templates = useTemplates()
  const turn = useSwitchClient()
  const turnMany = useSwitchClients()
  const may = holds(user, scopes.manageClients)
  const picked = Number(params.get("config") ?? 0)
  const find = params.get("find") ?? ""
  const all = clients.data ?? []
  const shown = all.filter((one) => (picked === 0 || one.configId === picked) && matches(one, find))
  const names = new Map((templates.data ?? []).map((one): [number, string] => [one.id, one.name]))
  const view = `${picked}|${find}`
  const [choice, setChoice] = useState({ view, keys: nothing })
  const [note, setNote] = useState({ view, told: quiet })
  const chosen = choice.view === view ? choice.keys : nothing
  const ids = shown.filter((one) => chosen.has(one.id)).map((one) => one.id)
  const told = note.view === view ? note.told : quiet

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

  function nameOf(id: number): string {
    return all.find((one) => one.id === id)?.name ?? `#${id}`
  }

  function actionsOf(one: Client): RowAction[] {
    return [
      { label: t("action.export"), onPick: () => navigate(`/clients/${one.id}/export`) },
      { label: t("action.settings"), onPick: () => navigate(`/clients/${one.id}/settings`) },
    ]
  }

  async function turnAll(on: boolean) {
    setNote({ view, told: quiet })
    try {
      const answer = await turnMany.mutateAsync({ ids, on })
      setChoice({ view, keys: nothing })
      setNote({ view, told: summary(t, answer, ids.length, on ? "clients.turnedOn" : "clients.turnedOff", nameOf) })
    } catch (error) {
      setNote({ view, told: { text: t(complaint(error)), title: "" } })
    }
  }

  return (
    <div className={`mt-4 ${card}`}>
      {shown.length === 0 && all.length === 0 && (
        <div className="px-4 py-6 text-sm text-muted">{t("clients.empty")}</div>
      )}

      {told.text.length > 0 && (
        <div className="border-b border-line px-4 py-2.5 text-sm text-warn" title={told.title || undefined}>
          {told.text}
        </div>
      )}

      {all.length > 0 && (
        <Rows
          name="client"
          items={shown}
          keyOf={(one) => one.id}
          choice={
            may
              ? {
                  chosen,
                  onChange: (keys) => setChoice({ view, keys }),
                  title: t("clients.choose"),
                  every: t("clients.chooseAll"),
                  actions: (
                    <>
                      <button
                        type="button"
                        disabled={turnMany.isPending}
                        onClick={() => void turnAll(true)}
                        className={tool}
                      >
                        {t("clients.turnOnChosen")}
                      </button>
                      <button
                        type="button"
                        disabled={turnMany.isPending}
                        onClick={() => void turnAll(false)}
                        className={tool}
                      >
                        {t("clients.turnOffChosen")}
                      </button>
                      <button
                        type="button"
                        onClick={() => navigate(`/clients/delete?ids=${ids.join(",")}`)}
                        className={toolRisky}
                      >
                        {t("clients.removeChosen")}
                      </button>
                    </>
                  ),
                }
              : undefined
          }
          tools={
            <div className="flex flex-wrap items-center gap-2">
              <select
                id="client-config"
                aria-label={t("clients.endpointName")}
                value={picked}
                onChange={(e) => put("config", e.target.value === "0" ? "" : e.target.value)}
                className={`w-full wide:w-56 ${fieldBox}`}
              >
                <option value={0}>{t("clients.everyConfig")}</option>
                {(configs.data ?? []).map((one) => (
                  <option key={one.id} value={one.id}>
                    {one.name}
                  </option>
                ))}
              </select>

              <Find value={find} onChange={(value) => put("find", value)} className="w-full wide:w-64" />
            </div>
          }
          columns={[
            {
              key: "on",
              caption: t("action.on"),
              width: 60,
              cell: (one) => (
                <Knob
                  value={one.isEnabled}
                  title={one.isEnabled ? t("action.turnOff") : t("action.turnOn")}
                  disabled={!may || turn.isPending}
                  onChange={(on) => void turn.mutateAsync({ id: one.id, on })}
                />
              ),
            },
            {
              key: "name",
              caption: t("clients.name"),
              sort: (one) => one.name,
              lead: true,
              body: "font-semibold text-ink",
              cell: (one) => (
                <span className="flex min-w-0 items-center gap-2">
                  <Link
                    to={`/clients/${one.id}/export`}
                    title={one.name}
                    className="max-w-full shrink-0 truncate hover:text-brand-ink"
                  >
                    {one.name}
                  </Link>
                  {one.state.isOnline && <span className="min-w-0 truncate text-xs text-good">{t("clients.online")}</span>}
                  {!one.isEnabled && <span className="min-w-0 truncate text-xs text-muted">{t("clients.off")}</span>}
                </span>
              ),
            },
            {
              key: "config",
              caption: t("clients.endpointName"),
              width: 112,
              sort: (one) => one.config,
              cell: (one) => one.config,
            },
            {
              key: "template",
              caption: t("clients.template"),
              width: 104,
              sort: (one) => named(one),
              cell: (one) =>
                one.templateId === null ? (
                  t("clients.dash")
                ) : (
                  <Link to={`/clients/templates/${one.templateId}/edit`} className="text-brand-ink hover:text-brand-lit">
                    {named(one)}
                  </Link>
                ),
            },
            {
              key: "address",
              caption: t("clients.address"),
              width: 128,
              sort: (one) => one.address.join(", "),
              cell: (one) => one.address.join(", "),
            },
            {
              key: "speed",
              caption: t("clients.speed"),
              width: 196,
              sort: (one) => one.state.txRate + one.state.rxRate,
              cell: (one) => <Speed one={one} />,
            },
            {
              key: "traffic",
              caption: t("clients.traffic"),
              width: 112,
              sort: (one) => one.state.used,
              cell: (one) => <Traffic one={one} />,
            },
            {
              key: "state",
              caption: t("clients.state"),
              width: 180,
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
              width: 56,
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
