import { useState } from "react"
import {
  draftOf as groupDraft,
  freshBalancer,
  useAddBalancer,
  useBalancers,
  useChangeBalancer,
  useRemoveBalancer,
  useSwitchBalancer,
} from "@/api/balancers"
import type { Balancer, BalancerMember } from "@/api/balancers"
import {
  draftOf as channelDraft,
  useAddOutbound,
  useApplyOutbound,
  useApplyOutbounds,
  useChangeOutbound,
  useFreshOutbound,
  useMoveOutbound,
  useOutbounds,
  useProbeOutbound,
  useRemoveOutbound,
  useSwitchOutbound,
} from "@/api/outbounds"
import type { Outbound, OutboundKind } from "@/api/outbounds"
import { scopes } from "@/api/scopes"
import { BalancerForm } from "@/components/BalancerForm"
import { Modal } from "@/components/Modal"
import { OutboundForm } from "@/components/OutboundForm"
import { RowActions } from "@/components/RowActions"
import { card, danger, secondary } from "@/components/styles"
import { bytes } from "@/format"
import { useLanguage, useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Channels() {
  const t = useText()
  const language = useLanguage()
  const user = useAppSelector((s) => s.auth.user)
  const outbounds = useOutbounds()
  const balancers = useBalancers()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<Outbound | null>(null)
  const [removing, setRemoving] = useState<Outbound | null>(null)
  const [grouping, setGrouping] = useState(false)
  const [regrouping, setRegrouping] = useState<Balancer | null>(null)
  const [ungrouping, setUngrouping] = useState<Balancer | null>(null)
  const fresh = useFreshOutbound(adding, nextName(outbounds.data), "wg")
  const add = useAddOutbound()
  const change = useChangeOutbound()
  const remove = useRemoveOutbound()
  const move = useMoveOutbound()
  const turn = useSwitchOutbound()
  const apply = useApplyOutbound()
  const applyAll = useApplyOutbounds()
  const probe = useProbeOutbound()
  const addGroup = useAddBalancer()
  const changeGroup = useChangeBalancer()
  const removeGroup = useRemoveBalancer()
  const turnGroup = useSwitchBalancer()
  const may = holds(user, scopes.manageRouting)
  const channels = outbounds.data ?? []
  const groups = balancers.data ?? []
  const last = channels.length - 1
  const loaded = outbounds.data !== undefined && balancers.data !== undefined

  return (
    <div>
      <div className={`mt-4 ${card}`}>
        {may && (
          <div className="flex justify-end gap-2 border-b border-line px-4 py-3">
            <button
              type="button"
              onClick={() => void applyAll.mutateAsync()}
              disabled={applyAll.isPending}
              className={secondary}
            >
              {applyAll.isPending ? t("outbounds.applying") : t("outbounds.apply")}
            </button>
            <RowActions
              title={t("outbounds.add")}
              trigger={t("outbounds.add")}
              actions={[
                { label: t("outbounds.channel"), onPick: () => setAdding(true) },
                ...(channels.length > 0 ? [{ label: t("outbounds.group"), onPick: () => setGrouping(true) }] : []),
              ]}
            />
          </div>
        )}

        {loaded && channels.length + groups.length === 0 && (
          <div className="px-4 py-6 text-sm text-muted">{t("outbounds.empty")}</div>
        )}

        {channels.length + groups.length > 0 && (
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
                {channels.map((outbound, at) => (
                  <tr key={`channel-${outbound.id}`} className="border-t border-line">
                    <td className="px-4 py-2 font-medium text-ink">
                      {outbound.name}
                      {!outbound.isEnabled && <span className="ml-2 text-xs text-muted">{t("outbounds.off")}</span>}
                    </td>
                    <td className="px-4 py-2 text-muted">{t(kindKey(outbound.kind))}</td>
                    <td className="px-4 py-2 text-muted">
                      {outbound.kind === "local" ? "" : `${outbound.host}:${outbound.port}`}
                    </td>
                    <td className="px-4 py-2 text-muted">
                      {outbound.mark} / {outbound.table}
                    </td>
                    <td className="px-4 py-2">
                      <ChannelState outbound={outbound} t={t} language={language} />
                    </td>
                    <td className="px-4 py-2 text-muted">
                      {outbound.state !== null && outbound.state.hasLink && outbound.kind !== "local"
                        ? `${bytes(t, outbound.state.rxBytes)} / ${bytes(t, outbound.state.txBytes)}`
                        : ""}
                    </td>
                    <td className="px-4 py-2">
                      {may && (
                        <RowActions
                          title={t("outbounds.actions")}
                          actions={[
                            { label: t("outbounds.apply"), onPick: () => void apply.mutateAsync(outbound.id) },
                            { label: t("outbounds.probeNow"), onPick: () => void probe.mutateAsync(outbound.id) },
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

                {groups.map((balancer) => (
                  <tr key={`group-${balancer.id}`} className="border-t border-line">
                    <td className="px-4 py-2 font-medium text-ink">
                      {balancer.name}
                      {!balancer.isEnabled && <span className="ml-2 text-xs text-muted">{t("balancers.off")}</span>}
                    </td>
                    <td className="px-4 py-2 text-muted">
                      {t("outbounds.group")} · {t(strategy(balancer))}
                    </td>
                    <td className="px-4 py-2">
                      <Members members={balancer.state.members} />
                    </td>
                    <td className="px-4 py-2" />
                    <td className="px-4 py-2">
                      <GroupState balancer={balancer} t={t} />
                    </td>
                    <td className="px-4 py-2" />
                    <td className="px-4 py-2">
                      {may && (
                        <RowActions
                          title={t("balancers.actions")}
                          actions={[
                            {
                              label: balancer.isEnabled ? t("balancers.turnOff") : t("balancers.turnOn"),
                              onPick: () => void turnGroup.mutateAsync({ id: balancer.id, on: !balancer.isEnabled }),
                            },
                            { label: t("balancers.edit"), onPick: () => setRegrouping(balancer) },
                            { label: t("balancers.remove"), onPick: () => setUngrouping(balancer), alarming: true },
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
          start={channelDraft(fresh.data)}
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
          start={channelDraft(editing)}
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
            {removing.kind === "local" ? t("outbounds.kindLocal") : `${removing.host}:${removing.port}`}
          </div>
        </Modal>
      )}

      {grouping && (
        <BalancerForm
          title={t("balancers.newTitle")}
          start={freshBalancer}
          pending={addGroup.isPending}
          error={addGroup.error}
          onSave={(draft) => void addGroup.mutateAsync(draft).then(() => setGrouping(false))}
          onClose={() => setGrouping(false)}
        />
      )}

      {regrouping && (
        <BalancerForm
          title={t("balancers.editTitle", { name: regrouping.name })}
          start={groupDraft(regrouping)}
          pending={changeGroup.isPending}
          error={changeGroup.error}
          onSave={(draft) =>
            void changeGroup.mutateAsync({ id: regrouping.id, draft }).then(() => setRegrouping(null))
          }
          onClose={() => setRegrouping(null)}
        />
      )}

      {ungrouping && (
        <Modal
          title={t("balancers.removeTitle", { name: ungrouping.name })}
          onClose={() => setUngrouping(null)}
          footer={
            <>
              <button type="button" onClick={() => setUngrouping(null)} className={secondary}>
                {t("balancers.cancel")}
              </button>
              <button
                type="button"
                onClick={() => void removeGroup.mutateAsync(ungrouping.id).then(() => setUngrouping(null))}
                disabled={removeGroup.isPending}
                className={danger}
              >
                {t("balancers.remove")}
              </button>
            </>
          }
        >
          <div className="text-sm text-muted">{ungrouping.members.join(", ")}</div>
        </Modal>
      )}
    </div>
  )
}

function ChannelState({ outbound, t, language }: { outbound: Outbound; t: Text; language: string }) {
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

  if (state.probe !== null && !state.probe.isReached) {
    return <span className="text-alarm">{t("outbounds.noProbe")}</span>
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

function GroupState({ balancer, t }: { balancer: Balancer; t: Text }) {
  if (!balancer.isEnabled) {
    return <span className="text-muted">{t("balancers.off")}</span>
  }

  if (!balancer.state.isLive) {
    return <span className="text-alarm">{t("error.noLiveMember")}</span>
  }

  return <span className="text-ink">{t("balancers.live")}</span>
}

function Members({ members }: { members: BalancerMember[] }) {
  return (
    <span className="flex flex-wrap gap-2">
      {members.map((member) => (
        <span
          key={member.name}
          className={member.isAlive ? "rounded bg-brand-soft px-2 py-0.5 text-brand-ink" : "rounded px-2 py-0.5 text-muted"}
        >
          {member.name}
        </span>
      ))}
    </span>
  )
}

function kindKey(kind: OutboundKind): TextKey {
  if (kind === "wg") {
    return "outbounds.kindWg"
  }

  return kind === "ws" ? "outbounds.kindWs" : "outbounds.kindLocal"
}

function strategy(balancer: Balancer): TextKey {
  if (balancer.strategy === "round") {
    return "balancers.round"
  }

  return balancer.strategy === "sticky" ? "balancers.sticky" : "balancers.priority"
}

function nextName(outbounds: Outbound[] | undefined): string {
  const taken = new Set((outbounds ?? []).map((one) => one.name))
  let at = 1
  while (taken.has(`out${at}`)) {
    at++
  }

  return `out${at}`
}
