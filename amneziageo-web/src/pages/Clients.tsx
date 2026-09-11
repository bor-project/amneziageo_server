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
import { Modal } from "@/components/Modal"
import { RowActions } from "@/components/RowActions"
import { card, danger, field, primary, secondary } from "@/components/styles"
import { bytes } from "@/format"
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
  const change = useChangeClient()
  const remove = useRemoveClient()
  const turn = useSwitchClient()
  const addDevice = useAddDevice()
  const may = holds(user, scopes.manageClients)
  const all = clients.data ?? []
  const shown = ordered(all.filter((one) => picked === 0 || one.configId === picked))
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
            <button
              type="button"
              onClick={() => setAdding(picked)}
              disabled={(configs.data?.length ?? 0) === 0}
              className={primary}
            >
              {t("clients.add")}
            </button>
          )}
        </div>

        {shown.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("clients.empty")}</div>}

        {shown.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-xs text-muted">
                <tr>
                  <th className="px-4 py-2 font-normal">{t("clients.name")}</th>
                  <th className="px-4 py-2 font-normal">{t("clients.endpointName")}</th>
                  <th className="px-4 py-2 font-normal">{t("clients.template")}</th>
                  <th className="px-4 py-2 font-normal">{t("clients.address")}</th>
                  <th className="px-4 py-2 font-normal">{t("clients.traffic")}</th>
                  <th className="px-4 py-2 font-normal">{t("clients.state")}</th>
                  <th className="px-4 py-2" />
                </tr>
              </thead>
              <tbody>
                {shown.map((one) => (
                  <tr key={one.id} className="border-t border-line">
                    <td className={`py-2 pr-4 font-medium text-ink ${one.parentId === null ? "pl-4" : "pl-10"}`}>
                      {one.name}
                      {!one.isEnabled && <span className="ml-2 text-xs text-muted">{t("clients.off")}</span>}
                    </td>
                    <td className="px-4 py-2 text-muted">{one.config}</td>
                    <td className="px-4 py-2 text-muted">{named(one)}</td>
                    <td className="px-4 py-2 text-muted">{one.address.join(", ")}</td>
                    <td className="px-4 py-2 text-muted">
                      {one.state.isPresent
                        ? `${bytes(t, one.state.rxBytes)} / ${bytes(t, one.state.txBytes)}`
                        : t("clients.dash")}
                    </td>
                    <td className="px-4 py-2">
                      <State one={one} t={t} language={language} />
                    </td>
                    <td className="px-4 py-2">
                      {may && <RowActions title={t("clients.actions")} actions={actionsOf(one)} />}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {adding !== null && <Adding configId={adding} onClose={() => setAdding(null)} />}

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
      <Modal title={t("clients.newTitle")} onClose={onClose} footer={null}>
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

function State({ one, t, language }: { one: Client; t: Text; language: string }) {
  return (
    <>
      <Seen one={one} t={t} language={language} />
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
