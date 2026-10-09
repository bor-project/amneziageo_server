import type { Client, ClientSignal, SignalError } from "@/api/clients"
import { bytes, rate } from "@/format"
import { useLanguage, useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"

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

export function Traffic({ one }: { one: Client }) {
  const t = useText()
  const used = bytes(t, one.state.used)

  if (one.dailyLimit > 0) {
    return <>{t("clients.usedOf", { used, limit: bytes(t, one.dailyLimit) })}</>
  }

  return <>{used}</>
}

export function Handshake({ one }: { one: Client }) {
  const t = useText()
  const language = useLanguage()

  return (
    <>
      {one.isEnabled && one.state.isSpent ? (
        <span className="text-warn">{t("clients.spent")}</span>
      ) : (
        <>{seen(one, t, language)}</>
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
