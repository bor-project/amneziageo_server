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
import { Rows } from "@/components/Rows"
import { card, danger, secondary } from "@/components/styles"
import { bytes } from "@/format"
import { useLanguage, useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

type Line = { kind: "channel"; channel: Outbound } | { kind: "group"; group: Balancer }

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
  const probe = useProbeOutbound()
  const addGroup = useAddBalancer()
  const changeGroup = useChangeBalancer()
  const removeGroup = useRemoveBalancer()
  const turnGroup = useSwitchBalancer()
  const may = holds(user, scopes.manageRouting)
  const channels = outbounds.data ?? []
  const groups = balancers.data ?? []
  const last = channels.length - 1
  const lines: Line[] = [
    ...channels.map((channel): Line => ({ kind: "channel", channel })),
    ...groups.map((group): Line => ({ kind: "group", group })),
  ]
  const loaded = outbounds.data !== undefined && balancers.data !== undefined

  return (
    <div>
      <div className={`mt-4 ${card}`}>
        {may && (
          <div className="flex justify-end gap-2 border-b border-line px-4 py-3">
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
          <Rows
            items={lines}
            keyOf={(line) => (line.kind === "channel" ? `channel-${line.channel.id}` : `group-${line.group.id}`)}
            columns={[
              {
                key: "name",
                caption: t("outbounds.name"),
                sort: (line) => (line.kind === "channel" ? line.channel.name : line.group.name),
                lead: true,
                body: "font-medium text-ink",
                cell: (line) =>
                  line.kind === "channel" ? (
                    <>
                      {line.channel.name}
                      {!line.channel.isEnabled && (
                        <span className="ml-2 text-xs text-muted">{t("outbounds.off")}</span>
                      )}
                    </>
                  ) : (
                    <>
                      {line.group.name}
                      {!line.group.isEnabled && <span className="ml-2 text-xs text-muted">{t("balancers.off")}</span>}
                    </>
                  ),
              },
              {
                key: "kind",
                caption: t("outbounds.kind"),
                sort: (line) =>
                  line.kind === "channel"
                    ? t(kindKey(line.channel.kind))
                    : `${t("outbounds.group")} · ${t(strategy(line.group))}`,
                body: "text-muted",
                cell: (line) =>
                  line.kind === "channel"
                    ? t(kindKey(line.channel.kind))
                    : `${t("outbounds.group")} · ${t(strategy(line.group))}`,
              },
              {
                key: "server",
                caption: t("outbounds.server"),
                sort: (line) =>
                  line.kind === "channel" && line.channel.kind !== "local"
                    ? `${line.channel.host}:${line.channel.port}`
                    : line.kind === "group"
                      ? line.group.state.members.map((member) => member.name).join(", ")
                      : null,
                body: "text-muted",
                cell: (line) =>
                  line.kind === "group" ? (
                    <Members members={line.group.state.members} />
                  ) : line.channel.kind === "local" ? (
                    ""
                  ) : (
                    `${line.channel.host}:${line.channel.port}`
                  ),
              },
              {
                key: "mark",
                caption: t("outbounds.mark"),
                sort: (line) => (line.kind === "channel" ? `${line.channel.mark} / ${line.channel.table}` : null),
                body: "text-muted",
                cell: (line) => (line.kind === "channel" ? `${line.channel.mark} / ${line.channel.table}` : ""),
              },
              {
                key: "state",
                caption: t("outbounds.state"),
                sort: (line) => (line.kind === "channel" ? channelRank(line.channel) : groupRank(line.group)),
                cell: (line) =>
                  line.kind === "channel" ? (
                    <ChannelState outbound={line.channel} t={t} language={language} />
                  ) : (
                    <GroupState balancer={line.group} t={t} />
                  ),
              },
              {
                key: "traffic",
                caption: t("outbounds.traffic"),
                sort: (line) =>
                  line.kind === "channel" &&
                  line.channel.state !== null &&
                  line.channel.state.hasLink &&
                  line.channel.kind !== "local"
                    ? line.channel.state.rxBytes + line.channel.state.txBytes
                    : null,
                body: "text-muted",
                cell: (line) =>
                  line.kind === "channel" &&
                  line.channel.state !== null &&
                  line.channel.state.hasLink &&
                  line.channel.kind !== "local"
                    ? `${bytes(t, line.channel.state.rxBytes)} / ${bytes(t, line.channel.state.txBytes)}`
                    : "",
              },
              {
                key: "actions",
                caption: t("outbounds.actions"),
                tail: true,
                cell: (line, at) =>
                  may &&
                  (line.kind === "channel" ? (
                    <RowActions
                      title={t("outbounds.actions")}
                      actions={[
                        { label: t("outbounds.apply"), onPick: () => void apply.mutateAsync(line.channel.id) },
                        { label: t("outbounds.probeNow"), onPick: () => void probe.mutateAsync(line.channel.id) },
                        {
                          label: line.channel.isEnabled ? t("outbounds.turnOff") : t("outbounds.turnOn"),
                          onPick: () => void turn.mutateAsync({ id: line.channel.id, on: !line.channel.isEnabled }),
                        },
                        { label: t("outbounds.edit"), onPick: () => setEditing(line.channel) },
                        ...(at > 0
                          ? [
                              {
                                label: t("outbounds.up"),
                                onPick: () => void move.mutateAsync({ id: line.channel.id, up: true }),
                              },
                            ]
                          : []),
                        ...(at < last
                          ? [
                              {
                                label: t("outbounds.down"),
                                onPick: () => void move.mutateAsync({ id: line.channel.id, up: false }),
                              },
                            ]
                          : []),
                        { label: t("outbounds.remove"), onPick: () => setRemoving(line.channel), alarming: true },
                      ]}
                    />
                  ) : (
                    <RowActions
                      title={t("balancers.actions")}
                      actions={[
                        {
                          label: line.group.isEnabled ? t("balancers.turnOff") : t("balancers.turnOn"),
                          onPick: () => void turnGroup.mutateAsync({ id: line.group.id, on: !line.group.isEnabled }),
                        },
                        { label: t("balancers.edit"), onPick: () => setRegrouping(line.group) },
                        { label: t("balancers.remove"), onPick: () => setUngrouping(line.group), alarming: true },
                      ]}
                    />
                  )),
              },
            ]}
          />
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

function channelRank(outbound: Outbound): number {
  const state = outbound.state

  if (state === null) {
    return 3
  }

  if (state.fault.length > 0) {
    return 1
  }

  if (!state.hasLink) {
    return 2
  }

  if (state.probe !== null && !state.probe.isReached) {
    return 4
  }

  if (outbound.kind !== "wg") {
    return 0
  }

  return state.lastHandshake === null ? 5 : 0
}

function groupRank(balancer: Balancer): number {
  if (!balancer.isEnabled) {
    return 2
  }

  return balancer.state.isLive ? 0 : 1
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
