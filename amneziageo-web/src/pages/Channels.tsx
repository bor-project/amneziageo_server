import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { useBalancers, useSwitchBalancer } from "@/api/balancers"
import type { Balancer, BalancerMember } from "@/api/balancers"
import {
  useApplyOutbound,
  useMoveOutbound,
  useOutbounds,
  useProbeOutbound,
  useSwitchOutbound,
} from "@/api/outbounds"
import type { Outbound, OutboundKind, OutboundState } from "@/api/outbounds"
import { scopes } from "@/api/scopes"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, fieldBox, secondary } from "@/components/styles"
import { bytes } from "@/format"
import { useLanguage, useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

type Line = { kind: "channel"; channel: Outbound } | { kind: "group"; group: Balancer }

export function Channels() {
  const t = useText()
  const language = useLanguage()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const [params, setParams] = useSearchParams()
  const outbounds = useOutbounds()
  const balancers = useBalancers()
  const move = useMoveOutbound()
  const turn = useSwitchOutbound()
  const apply = useApplyOutbound()
  const probe = useProbeOutbound()
  const turnGroup = useSwitchBalancer()
  const may = holds(user, scopes.manageRouting)
  const find = params.get("find") ?? ""
  const channels = outbounds.data ?? []
  const groups = balancers.data ?? []
  const last = channels.length - 1
  const lines: Line[] = [
    ...channels.map((channel): Line => ({ kind: "channel", channel })),
    ...groups.map((group): Line => ({ kind: "group", group })),
  ]
  const shown = lines.filter((line) => matches(line, find))
  const loaded = outbounds.data !== undefined && balancers.data !== undefined

  function put(key: string, value: string) {
    const kept = new URLSearchParams(params)

    if (value === "") {
      kept.delete(key)
    } else {
      kept.set(key, value)
    }

    setParams(kept, { replace: true })
  }

  return (
    <div className={`mt-4 ${card}`}>
      {loaded && lines.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("outbounds.empty")}</div>}

      {lines.length > 0 && (
        <Rows
          name="channel"
          items={shown}
          keyOf={(line) => (line.kind === "channel" ? `channel-${line.channel.id}` : `group-${line.group.id}`)}
          tools={
            <div className="flex flex-wrap items-center gap-2">
              <input
                value={find}
                placeholder={t("action.search")}
                onChange={(e) => put("find", e.target.value)}
                className={`w-full wide:w-60 ${fieldBox}`}
              />
              {may && channels.length > 0 && (
                <Link to="/routing/channels/groups/new" className={`flex h-10 shrink-0 items-center ${secondary}`}>
                  {t("outbounds.group")}
                </Link>
              )}
            </div>
          }
          columns={[
            {
              key: "name",
              caption: t("outbounds.name"),
              sort: (line) => (line.kind === "channel" ? line.channel.name : line.group.name),
              lead: true,
              body: "font-semibold text-ink",
              cell: (line) =>
                line.kind === "channel" ? (
                  <>
                    {may ? (
                      <Link to={`/routing/channels/${line.channel.id}/edit`} className="hover:text-brand-ink">
                        {line.channel.name}
                      </Link>
                    ) : (
                      line.channel.name
                    )}
                    {!line.channel.isEnabled && <span className="ml-2 text-xs text-muted">{t("outbounds.off")}</span>}
                  </>
                ) : (
                  <>
                    {may ? (
                      <Link to={`/routing/channels/groups/${line.group.id}/edit`} className="hover:text-brand-ink">
                        {line.group.name}
                      </Link>
                    ) : (
                      line.group.name
                    )}
                    {!line.group.isEnabled && <span className="ml-2 text-xs text-muted">{t("balancers.off")}</span>}
                  </>
                ),
            },
            {
              key: "kind",
              caption: t("outbounds.kind"),
              sort: (line) => named(t, line),
              cell: (line) => named(t, line),
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
              sort: (line) => sum(carried(line)),
              cell: (line) => told(t, carried(line)),
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
                      {
                        label: t("outbounds.edit"),
                        onPick: () => navigate(`/routing/channels/${line.channel.id}/edit`),
                      },
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
                      {
                        label: t("outbounds.remove"),
                        onPick: () => navigate(`/routing/channels/${line.channel.id}/delete`),
                        alarming: true,
                      },
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
                      {
                        label: t("balancers.edit"),
                        onPick: () => navigate(`/routing/channels/groups/${line.group.id}/edit`),
                      },
                      {
                        label: t("balancers.remove"),
                        onPick: () => navigate(`/routing/channels/groups/${line.group.id}/delete`),
                        alarming: true,
                      },
                    ]}
                  />
                )),
            },
          ]}
        />
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
    return <span className="text-good">{t("outbounds.ready")}</span>
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

  return <span className="text-good">{t("balancers.live")}</span>
}

function Members({ members }: { members: BalancerMember[] }) {
  return (
    <span className="flex flex-wrap gap-2">
      {members.map((member) => (
        <span
          key={member.name}
          className={member.isAlive ? "rounded bg-chip px-2 py-0.5 text-chip-ink" : "rounded px-2 py-0.5 text-muted"}
        >
          {member.name}
        </span>
      ))}
    </span>
  )
}

function carried(line: Line): OutboundState | null {
  if (line.kind !== "channel" || line.channel.kind === "local") {
    return null
  }

  const state = line.channel.state

  return state !== null && state.hasLink ? state : null
}

function sum(state: OutboundState | null): number | null {
  return state === null ? null : state.rxBytes + state.txBytes
}

function told(t: Text, state: OutboundState | null): string {
  return state === null ? "" : `${bytes(t, state.rxBytes)} / ${bytes(t, state.txBytes)}`
}

function named(t: Text, line: Line): string {
  return line.kind === "channel"
    ? t(kindKey(line.channel.kind))
    : `${t("outbounds.group")} · ${t(strategy(line.group))}`
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

function matches(line: Line, find: string): boolean {
  const query = find.trim().toLowerCase()

  if (query === "") {
    return true
  }

  if (line.kind === "group") {
    return (
      line.group.name.toLowerCase().includes(query) ||
      line.group.members.some((member) => member.toLowerCase().includes(query))
    )
  }

  return (
    line.channel.name.toLowerCase().includes(query) ||
    line.channel.host.toLowerCase().includes(query) ||
    String(line.channel.port).includes(query)
  )
}
