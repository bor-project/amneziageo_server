import type { Balancer, BalancerMember } from "@/api/balancers"
import { useOutbounds } from "@/api/outbounds"
import { useText } from "@/i18n"
import type { Outbound } from "@/api/outbounds"
import type { Text, TextKey } from "@/i18n"

type ChannelStatus = { text: TextKey; tone: string; rank: number }
export function ChannelState({ outbound, t, language }: { outbound: Outbound; t: Text; language: string }) {
  const status = channelStatus(outbound)
  return (
    <span className={status.tone} title={channelTitle(outbound, status, t, language)}>
      {t(status.text)}
    </span>
  )
}

function channelStatus(outbound: Outbound): ChannelStatus {
  if (!outbound.isEnabled) {
    return { text: "outbounds.off", tone: "text-muted", rank: 7 }
  }

  const state = outbound.state
  if (state === null) {
    return { text: "outbounds.unknown", tone: "text-muted", rank: 3 }
  }

  if (state.fault.length > 0) {
    return { text: "outbounds.faulted", tone: "text-alarm", rank: 1 }
  }

  if (!state.hasLink) {
    return { text: "outbounds.notUp", tone: "text-muted", rank: 2 }
  }

  if (outbound.kind !== "local") {
    if (state.lastHandshake === null) {
      return { text: "outbounds.noHandshake", tone: "text-muted", rank: 5 }
    }

    if (!state.isAlive) {
      return { text: "outbounds.staleHandshake", tone: "text-muted", rank: 5 }
    }
  }

  if (outbound.probe.trim() === "") {
    return { text: "outbounds.probeOff", tone: "text-muted", rank: 3 }
  }

  const probe = state.probe
  if (probe === null) {
    return { text: "outbounds.unknown", tone: "text-muted", rank: 3 }
  }

  const health = probe.health ?? (probe.isReached ? "healthy" : "unavailable")
  if (health === "degraded") {
    return { text: "outbounds.probeDegraded", tone: "text-warn", rank: 6 }
  }

  if (health === "unavailable") {
    return { text: "outbounds.noProbe", tone: "text-alarm", rank: 4 }
  }

  return health === "healthy"
    ? { text: "outbounds.ready", tone: "text-good", rank: 0 }
    : { text: "outbounds.unknown", tone: "text-muted", rank: 3 }
}

function channelTitle(outbound: Outbound, status: ChannelStatus, t: Text, language: string): string | undefined {
  const state = outbound.state
  if (status.text === "outbounds.faulted") {
    return state?.fault
  }

  if (status.text === "outbounds.probeDegraded" || status.text === "outbounds.noProbe") {
    const probe = state?.probe
    return probe?.attempts !== undefined && probe.missed !== undefined && probe.attempts > 0
      ? t("outbounds.probeMissed", { missed: probe.missed, attempts: probe.attempts })
      : undefined
  }

  if (status.text === "outbounds.unknown" && state?.hasLink && outbound.probe.trim() !== "") {
    return t("outbounds.probePending")
  }

  return state?.lastHandshake != null
    && (status.text === "outbounds.ready" || status.text === "outbounds.staleHandshake" || status.text === "outbounds.probeOff")
    ? new Date(state.lastHandshake).toLocaleString(language)
    : undefined
}

export function GroupState({ balancer, t }: { balancer: Balancer; t: Text }) {
  if (!balancer.isEnabled) {
    return <span className="text-muted">{t("balancers.off")}</span>
  }

  if (!balancer.state.isLive) {
    return <span className="text-alarm">{t("error.noLiveMember")}</span>
  }

  return <span className="text-good">{t("balancers.live")}</span>
}

export function Members({ members }: { members: BalancerMember[] }) {
  const t = useText()
  const outbounds = useOutbounds().data ?? []

  return (
    <span className="flex flex-wrap gap-2">
      {members.map((member) => (
        <span
          key={member.name}
          className={member.isAlive ? "rounded bg-chip px-2 py-0.5 text-chip-ink" : "rounded px-2 py-0.5 text-muted"}
        >
          {outbounds.some((one) => one.name === member.name && one.kind === "local") ? `${t("rules.actionDirect")} (${member.name})` : member.name}
        </span>
      ))}
    </span>
  )
}
