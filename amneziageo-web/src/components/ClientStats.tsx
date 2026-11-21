import type { Client, ClientSignal, SignalError } from "@/api/clients"
import { bytes, rate } from "@/format"
import { useLanguage, useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { trafficView } from "@/components/clientTraffic"

const signalWords: Record<SignalError, TextKey> = {
  "no-address": "clients.signalNoAddress",
  "no-answer": "clients.signalNoAnswer",
  refused: "clients.signalRefused",
  unreachable: "clients.signalUnreachable",
  "bad-answer": "clients.signalBadAnswer",
}

export function Speed({ one }: { one: Client }) {
  const t = useText()

  if (!one.state.isOnline && one.state.rxRate === 0 && one.state.txRate === 0) {
    return <span className="text-muted">{t("clients.dash")}</span>
  }

  return (
    <span className="whitespace-nowrap">
      <span title={t("clients.toClient")}>{`↓ ${rate(t, one.state.txRate)}`}</span>
      <span className="ml-3" title={t("clients.fromClient")}>{`↑ ${rate(t, one.state.rxRate)}`}</span>
    </span>
  )
}

export function Traffic({ one, total, onToggle }: { one: Client; total: boolean; onToggle: () => void }) {
  const t = useText()
  const shown = trafficView(one, total)
  const used = shown.value === null ? t("clients.dash") : bytes(t, shown.value)
  const text = shown.cumulative ? used : t("clients.usedOf", { used, limit: bytes(t, shown.quota) })

  if (shown.quota <= 0) {
    return <span className="block max-w-full truncate px-0.5" title={`${text} · ${t("clientInfo.totalTraffic")}`}>{text}</span>
  }

  return <button
    type="button"
    className="cell-value block w-full min-w-0 truncate rounded px-0.5 text-left hover:text-brand-ink"
    aria-pressed={shown.cumulative}
    title={`${text} · ${t(shown.cumulative ? "clientInfo.showQuota" : "clientInfo.showTotal")}`}
    onClick={onToggle}
  >{text}</button>
}

export function Handshake({ one }: { one: Client }) {
  const t = useText()
  const language = useLanguage()

  return (
    <>
      {one.isEnabled && one.state.limits?.isBlocked ? (
        <span className="text-alarm">{t(one.state.limits.isExhausted ? "trafficLimits.quotaBlocked" : "trafficLimits.blocked")}</span>
      ) : one.isEnabled && one.state.limits?.isThrottled ? (
        <span className="text-warn">{t("trafficLimits.throttled")}</span>
      ) : one.isEnabled && !one.state.limits && one.state.isSpent ? (
        <span className="text-warn">{t("clients.spent")}</span>
      ) : (
        <>{seen(one, t, language)}</>
      )}
      {one.state.limits && !one.state.limits.applied && (
        <div className="text-xs text-warn" title={one.state.limits.error || undefined}>
          {t("trafficLimits.notApplied")}
        </div>
      )}
      {one.state.cut.length > 0 && (
        <div className="text-xs text-warn">{t("clients.cut", { list: one.state.cut.join(", ") })}</div>
      )}
      {one.signal && (
        <div className="text-xs text-warn" title={missed(one.signal, t, language)}>
          {t("clients.signalMissed")}
        </div>
      )}
    </>
  )
}

function missed(signal: ClientSignal, t: Text, language: string) {
  const word = signalWords[signal.error as SignalError]

  return t("clients.signalTold", {
    why: word ? t(word) : signal.message,
    time: new Date(signal.at).toLocaleString(language),
  })
}

function seen(one: Client, t: Text, language: string) {
  if (!one.state.isPresent) {
    return <span className="text-muted">{t("clients.notOnHost")}</span>
  }

  if (one.state.lastHandshake === null) {
    return <span className="text-muted">{t("clients.noHandshake")}</span>
  }

  return <span>{new Date(one.state.lastHandshake).toLocaleString(language)}</span>
}
