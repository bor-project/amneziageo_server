import { useState } from "react"
import {
  draftOf,
  useAddClient,
  useAddDevice,
  useChangeClient,
  useClientDraft,
  useClients,
  useRemoveClient,
  useSwitchClient,
} from "@/api/clients"
import type { Client } from "@/api/clients"
import { useConfigs } from "@/api/configs"
import { scopes } from "@/api/scopes"
import { useTemplates } from "@/api/templates"
import { ClientConfig } from "@/components/ClientConfig"
import { ClientForm } from "@/components/ClientForm"
import { ClientImport } from "@/components/ClientImport"
import { Modal } from "@/components/Modal"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, danger, field, primary, secondary } from "@/components/styles"
import { bytes, rate } from "@/format"
import { useLanguage, useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Clients() {
  const t = useText()
  const language = useLanguage()
  const user = useAppSelector((s) => s.auth.user)
  const configs = useConfigs()
  const clients = useClients()
  const templates = useTemplates()
  const [picked, setPicked] = useState(0)
  const [adding, setAdding] = useState<number | null>(null)
  const [editing, setEditing] = useState<Client | null>(null)
  const [removing, setRemoving] = useState<Client | null>(null)
  const [showing, setShowing] = useState<Client | null>(null)
  const [importing, setImporting] = useState(false)
  const change = useChangeClient()
  const remove = useRemoveClient()
  const turn = useSwitchClient()
  const addDevice = useAddDevice()
  const may = holds(user, scopes.manageClients)
  const all = clients.data ?? []
  const shown = all.filter((one) => picked === 0 || one.configId === picked)
  const names = new Map((templates.data ?? []).map((one): [number, string] => [one.id, one.name]))

  function named(one: Client): string {
    return (one.templateId === null ? undefined : names.get(one.templateId)) ?? t("clients.dash")
  }

  function actionsOf(one: Client) {
    const actions = [
      { label: t("clients.config"), onPick: () => setShowing(one) },
      {
        label: one.isEnabled ? t("clients.turnOff") : t("clients.turnOn"),
        onPick: () => void turn.mutateAsync({ id: one.id, on: !one.isEnabled }),
      },
    ]
    if (one.parentId === null && one.multiDevice) {
      actions.push({ label: t("clients.addDevice"), onPick: () => void addDevice.mutateAsync(one.id).then(setShowing) })
    }

    if (one.parentId === null) {
      actions.push({ label: t("clients.edit"), onPick: () => setEditing(one) })
    }

    return [...actions, { label: t("clients.remove"), onPick: () => setRemoving(one), alarming: true }]
  }

  return (
    <div>
      <div className={`mt-4 ${card}`}>
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-4 py-3">
          <select
            id="client-config"
            value={picked}
            onChange={(e) => setPicked(Number(e.target.value))}
            className={`${field} max-w-60`}
          >
            <option value={0}>{t("clients.everyConfig")}</option>
            {(configs.data ?? []).map((one) => (
              <option key={one.id} value={one.id}>
                {one.name}
              </option>
            ))}
          </select>

          {may && (
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => setImporting(true)}
                disabled={(configs.data?.length ?? 0) === 0}
                className={secondary}
              >
                {t("clients.import")}
              </button>
              <button
                type="button"
                onClick={() => setAdding(picked)}
                disabled={(configs.data?.length ?? 0) === 0}
                className={primary}
              >
                {t("clients.add")}
              </button>
            </div>
          )}
        </div>

        {shown.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("clients.empty")}</div>}

        {shown.length > 0 && (
          <Rows
            items={shown}
            arrange={ordered}
            keyOf={(one) => one.id}
            columns={[
              {
                key: "name",
                caption: t("clients.name"),
                sort: (one) => one.name,
                lead: true,
                body: "font-medium text-ink",
                cell: (one) => (
                  <span className={one.parentId === null ? "" : "pl-6"}>
                    {one.name}
                    {one.state.isOnline && <span className="ml-2 text-xs text-brand">{t("clients.online")}</span>}
                    {!one.isEnabled && <span className="ml-2 text-xs text-muted">{t("clients.off")}</span>}
                  </span>
                ),
              },
              {
                key: "config",
                caption: t("clients.endpointName"),
                sort: (one) => one.config,
                body: "text-muted",
                cell: (one) => one.config,
              },
              {
                key: "template",
                caption: t("clients.template"),
                sort: (one) => named(one),
                body: "text-muted",
                cell: (one) => named(one),
              },
              {
                key: "address",
                caption: t("clients.address"),
                sort: (one) => one.address.join(", "),
                body: "text-muted",
                cell: (one) => one.address.join(", "),
              },
              {
                key: "speed",
                caption: t("clients.speed"),
                sort: (one) => one.state.txRate + one.state.rxRate,
                cell: (one) => <Speed one={one} t={t} />,
              },
              {
                key: "traffic",
                caption: t("clients.traffic"),
                sort: (one) => (one.parentId === null ? one.state.used : one.state.todayRx + one.state.todayTx),
                body: "whitespace-nowrap text-muted",
                cell: (one) => <Traffic one={one} t={t} />,
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
                cell: (one) => <State one={one} t={t} language={language} />,
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

      {adding !== null && <Adding configId={adding} onClose={() => setAdding(null)} />}

      {importing && <ClientImport configs={configs.data ?? []} start={picked} onClose={() => setImporting(false)} />}

      {editing && (
        <ClientForm
          title={t("clients.editTitle", { name: editing.name })}
          start={draftOf(editing)}
          self={editing.id}
          pending={change.isPending}
          error={change.error}
          onSave={(draft) => void change.mutateAsync({ id: editing.id, draft }).then(() => setEditing(null))}
          onClose={() => setEditing(null)}
        />
      )}

      {showing && (
        <ClientConfig
          id={showing.id}
          title={t("clients.configTitle", { name: showing.name })}
          onClose={() => setShowing(null)}
        />
      )}

      {removing && (
        <Modal
          title={t("clients.removeTitle", { name: removing.name })}
          onClose={() => setRemoving(null)}
          footer={
            <>
              <button type="button" onClick={() => setRemoving(null)} className={secondary}>
                {t("clients.cancel")}
              </button>
              <button
                type="button"
                onClick={() => void remove.mutateAsync(removing.id).then(() => setRemoving(null))}
                disabled={remove.isPending}
                className={danger}
              >
                {t("clients.remove")}
              </button>
            </>
          }
        >
          <div className="flex flex-col gap-1 text-sm text-muted">
            <div>{removing.address.join(", ")}</div>
            {all
              .filter((one) => one.parentId === removing.id)
              .map((one) => (
                <div key={one.id}>{`${one.name}: ${one.address.join(", ")}`}</div>
              ))}
          </div>
        </Modal>
      )}
    </div>
  )
}

function Adding({ configId, onClose }: { configId: number; onClose: () => void }) {
  const t = useText()
  const draft = useClientDraft()
  const add = useAddClient()

  if (draft.data === undefined) {
    return (
      <Modal title={t("clients.newTitle")} wide onClose={onClose} footer={null}>
        <div className="text-sm text-muted">{t("clients.busy")}</div>
      </Modal>
    )
  }

  return (
    <ClientForm
      title={t("clients.newTitle")}
      start={{ ...draftOf(draft.data), configId, address: [] }}
      pending={add.isPending}
      error={add.error}
      onSave={(body) => void add.mutateAsync(body).then(onClose)}
      onClose={onClose}
    />
  )
}

function Speed({ one, t }: { one: Client; t: Text }) {
  if (!one.state.isOnline && one.state.rxRate === 0 && one.state.txRate === 0) {
    return <span className="text-muted">{t("clients.dash")}</span>
  }

  return (
    <span className="whitespace-nowrap text-ink">
      <span title={t("clients.toClient")}>{`↓ ${rate(t, one.state.txRate)}`}</span>
      <span className="ml-3" title={t("clients.fromClient")}>{`↑ ${rate(t, one.state.rxRate)}`}</span>
    </span>
  )
}

function Traffic({ one, t }: { one: Client; t: Text }) {
  const used = bytes(t, one.parentId === null ? one.state.used : one.state.todayRx + one.state.todayTx)

  if (one.parentId === null && one.dailyLimit > 0) {
    return <>{t("clients.usedOf", { used, limit: bytes(t, one.dailyLimit) })}</>
  }

  return <>{used}</>
}

function State({ one, t, language }: { one: Client; t: Text; language: string }) {
  return (
    <>
      {one.isEnabled && one.state.isSpent ? (
        <span className="text-warn">{t("clients.spent")}</span>
      ) : (
        <Seen one={one} t={t} language={language} />
      )}
      {one.state.cut.length > 0 && (
        <div className="text-xs text-warn">{t("clients.cut", { list: one.state.cut.join(", ") })}</div>
      )}
    </>
  )
}

function Seen({ one, t, language }: { one: Client; t: Text; language: string }) {
  if (!one.state.isPresent) {
    return <span className="text-muted">{t("clients.notOnHost")}</span>
  }

  if (one.state.lastHandshake === null) {
    return <span className="text-muted">{t("clients.noHandshake")}</span>
  }

  return (
    <span className={one.state.isOnline ? "text-ink" : "text-muted"}>
      {new Date(one.state.lastHandshake).toLocaleString(language)}
    </span>
  )
}

function ordered(clients: Client[]): Client[] {
  const tops = clients.filter((one) => one.parentId === null)
  const known = new Set(tops.map((one) => one.id))
  const rows = tops.flatMap((top) => [top, ...clients.filter((one) => one.parentId === top.id)])

  return [...rows, ...clients.filter((one) => one.parentId !== null && !known.has(one.parentId))]
}
