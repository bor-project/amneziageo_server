import { useState } from "react"
import {
  draftOf,
  useAddOutbound,
  useApplyOutbound,
  useApplyOutbounds,
  useChangeOutbound,
  useFreshOutbound,
  useMoveOutbound,
  useOutbounds,
  useRemoveOutbound,
  useSwitchOutbound,
} from "@/api/outbounds"
import type { Outbound } from "@/api/outbounds"
import { scopes } from "@/api/scopes"
import { Modal } from "@/components/Modal"
import { OutboundForm } from "@/components/OutboundForm"
import { RowActions } from "@/components/RowActions"
import { card, danger, primary, secondary } from "@/components/styles"
import { bytes } from "@/format"
import { useLanguage, useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Outbounds() {
  const t = useText()
  const language = useLanguage()
  const user = useAppSelector((s) => s.auth.user)
  const outbounds = useOutbounds()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<Outbound | null>(null)
  const [removing, setRemoving] = useState<Outbound | null>(null)
  const fresh = useFreshOutbound(adding, nextName(outbounds.data))
  const add = useAddOutbound()
  const change = useChangeOutbound()
  const remove = useRemoveOutbound()
  const move = useMoveOutbound()
  const turn = useSwitchOutbound()
  const apply = useApplyOutbound()
  const applyAll = useApplyOutbounds()
  const may = holds(user, scopes.manageRouting)
  const last = (outbounds.data?.length ?? 0) - 1

  return (
    <div>
      <h1 className="text-xl font-semibold">{t("nav.outbounds")}</h1>

      <div className={`mt-4 ${card}`}>
        {may && (
          <div className="flex justify-end gap-2 border-b border-line px-4 py-3">
            <button
              type="button"
              onClick={() => void applyAll.mutateAsync()}
              disabled={applyAll.isPending}
              className={secondary}
            >
              {applyAll.isPending ? t("outbounds.applying") : t("outbounds.applyAll")}
            </button>
            <button type="button" onClick={() => setAdding(true)} className={primary}>
              {t("outbounds.add")}
            </button>
          </div>
        )}

        {outbounds.data?.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("outbounds.empty")}</div>}

        {outbounds.data && outbounds.data.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-xs text-muted">
                <tr>
                  <th className="px-4 py-2 font-normal">{t("outbounds.name")}</th>
                  <th className="px-4 py-2 font-normal">{t("outbounds.kind")}</th>
                  <th className="px-4 py-2 font-normal">{t("outbounds.server")}</th>
                  <th className="px-4 py-2 font-normal">{t("outbounds.mark")}</th>
                  <th className="px-4 py-2 font-normal">{t("outbounds.state")}</th>
                  <th className="px-4 py-2 font-normal">{t("outbounds.traffic")}</th>
                  <th className="px-4 py-2" />
                </tr>
              </thead>
              <tbody>
                {outbounds.data.map((outbound, at) => (
                  <tr key={outbound.id} className="border-t border-line">
                    <td className="px-4 py-2 font-medium text-ink">
                      {outbound.name}
                      {!outbound.isEnabled && <span className="ml-2 text-xs text-muted">{t("outbounds.off")}</span>}
                    </td>
                    <td className="px-4 py-2 text-muted">
                      {outbound.kind === "wg" ? t("outbounds.kindWg") : t("outbounds.kindLocal")}
                    </td>
                    <td className="px-4 py-2 text-muted">
                      {outbound.kind === "wg" ? `${outbound.host}:${outbound.port}` : ""}
                    </td>
                    <td className="px-4 py-2 text-muted">
                      {outbound.mark} / {outbound.table}
                    </td>
                    <td className="px-4 py-2">
                      <State outbound={outbound} t={t} language={language} />
                    </td>
                    <td className="px-4 py-2 text-muted">
                      {outbound.state !== null && outbound.state.hasLink && outbound.kind === "wg"
                        ? `${bytes(t, outbound.state.rxBytes)} / ${bytes(t, outbound.state.txBytes)}`
                        : ""}
                    </td>
                    <td className="px-4 py-2">
                      {may && (
                        <RowActions
                          title={t("outbounds.actions")}
                          actions={[
                            { label: t("outbounds.apply"), onPick: () => void apply.mutateAsync(outbound.id) },
                            {
                              label: outbound.isEnabled ? t("outbounds.turnOff") : t("outbounds.turnOn"),
                              onPick: () => void turn.mutateAsync({ id: outbound.id, on: !outbound.isEnabled }),
                            },
                            { label: t("outbounds.edit"), onPick: () => setEditing(outbound) },
                            ...(at > 0
                              ? [{ label: t("outbounds.up"), onPick: () => void move.mutateAsync({ id: outbound.id, up: true }) }]
                              : []),
                            ...(at < last
                              ? [{ label: t("outbounds.down"), onPick: () => void move.mutateAsync({ id: outbound.id, up: false }) }]
                              : []),
                            { label: t("outbounds.remove"), onPick: () => setRemoving(outbound), alarming: true },
                          ]}
                        />
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {adding && fresh.data && (
        <OutboundForm
          title={t("outbounds.newTitle")}
          start={draftOf(fresh.data)}
          publicKey={fresh.data.publicKey}
          pending={add.isPending}
          error={add.error}
          onSave={(draft) => void add.mutateAsync(draft).then(() => setAdding(false))}
          onClose={() => setAdding(false)}
        />
      )}

      {editing && (
        <OutboundForm
          title={t("outbounds.editTitle", { name: editing.name })}
          start={draftOf(editing)}
          publicKey={editing.publicKey}
          pending={change.isPending}
          error={change.error}
          onSave={(draft) => void change.mutateAsync({ id: editing.id, draft }).then(() => setEditing(null))}
          onClose={() => setEditing(null)}
        />
      )}

      {removing && (
        <Modal
          title={t("outbounds.removeTitle", { name: removing.name })}
          onClose={() => setRemoving(null)}
          footer={
            <>
              <button type="button" onClick={() => setRemoving(null)} className={secondary}>
                {t("outbounds.cancel")}
              </button>
              <button
                type="button"
                onClick={() => void remove.mutateAsync(removing.id).then(() => setRemoving(null))}
                disabled={remove.isPending}
                className={danger}
              >
                {t("outbounds.remove")}
              </button>
            </>
          }
        >
          <div className="text-sm text-muted">
            {removing.kind === "wg" ? `${removing.host}:${removing.port}` : t("outbounds.kindLocal")}
          </div>
        </Modal>
      )}
    </div>
  )
}

function State({ outbound, t, language }: { outbound: Outbound; t: Text; language: string }) {
  const state = outbound.state
  if (state === null) {
    return <span className="text-muted">{t("outbounds.unknown")}</span>
  }

  if (state.fault.length > 0) {
    return (
      <span className="text-alarm" title={state.fault}>
        {t("outbounds.faulted")}
      </span>
    )
  }

  if (!state.hasLink) {
    return <span className="text-muted">{t("outbounds.notUp")}</span>
  }

  if (outbound.kind !== "wg") {
    return <span className="text-ink">{t("outbounds.ready")}</span>
  }

  return (
    <span className={state.isAlive ? "text-ink" : "text-muted"}>
      {state.lastHandshake === null
        ? t("outbounds.noHandshake")
        : new Date(state.lastHandshake).toLocaleString(language)}
    </span>
  )
}

function nextName(outbounds: Outbound[] | undefined): string {
  const taken = new Set((outbounds ?? []).map((one) => one.name))
  let at = 1
  while (taken.has(`out${at}`)) {
    at++
  }

  return `out${at}`
}
