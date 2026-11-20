import { useState } from "react"
import axios from "axios"
import type { Client } from "@/api/clients"
import { useResetClientLimits } from "@/api/clients"
import { complaint } from "@/api/auth"
import { Dialog } from "@/components/Dialog"
import { danger, secondary, card, toolRisky } from "@/components/styles"
import { bytes } from "@/format"
import { useLanguage, useText } from "@/i18n"

export function ClientLimits({ one, editable }: { one: Client; editable: boolean }) {
  const t = useText()
  const language = useLanguage()
  const reset = useResetClientLimits()
  const [asking, setAsking] = useState(false)
  const limits = one.state.limits

  if (!limits?.policy.enabled) {
    return null
  }

  const metered = limits.policy.quotaBytes > 0
  const failed = reset.error === null ? "" : resetFailure(reset.error)
  const speed = (value: number) => value === 0
    ? t("clients.noLimit")
    : new Intl.NumberFormat(language, { maximumFractionDigits: 6 }).format(value / 1_000_000)
  const date = (value: string) => `${new Date(value).toLocaleString(language, { timeZone: "UTC" })} UTC`

  function resetFailure(error: unknown): string {
    const detail = axios.isAxiosError(error)
      ? (error.response?.data as { error?: string; message?: string } | undefined)
      : undefined

    return detail?.error === "client-limits-not-applied" && detail.message
      ? detail.message
      : t(complaint(error))
  }

  function close() {
    if (!reset.isPending) {
      setAsking(false)
    }
  }

  return (
    <div className={`mt-4 p-4.5 ${card}`}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-sm font-semibold text-ink-soft">{t("trafficLimits.current")}</h2>
        {editable && metered && (
          <button
            type="button"
            className={toolRisky}
            disabled={reset.isPending}
            onClick={() => { reset.reset(); setAsking(true) }}
          >
            {t("trafficLimits.reset")}
          </button>
        )}
      </div>
      <dl className="mt-3 grid grid-cols-1 gap-3.5 sm:grid-cols-2 xl:grid-cols-4">
        <div>
          <dt className="text-xs text-muted">{t("trafficLimits.download")}</dt>
          <dd className="mt-1 text-sm text-ink">{speed(limits.downloadBitsPerSecond)}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted">{t("trafficLimits.upload")}</dt>
          <dd className="mt-1 text-sm text-ink">{speed(limits.uploadBitsPerSecond)}</dd>
        </div>
        {metered && (
          <>
            <div>
              <dt className="text-xs text-muted">{t("trafficLimits.used")}</dt>
              <dd className="mt-1 text-sm text-ink">{t("clients.usedOf", { used: bytes(t, limits.usedBytes), limit: bytes(t, limits.policy.quotaBytes) })}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted">{t("trafficLimits.remaining")}</dt>
              <dd className="mt-1 text-sm text-ink">{limits.remainingBytes === null ? t("clients.noLimit") : bytes(t, limits.remainingBytes)}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted">{t("trafficLimits.period")}</dt>
              <dd className="mt-1 text-sm text-ink" title={date(limits.startedUtc)}>{t(`trafficLimits.${limits.policy.period}`)}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted">{t("trafficLimits.nextReset")}</dt>
              <dd className="mt-1 text-sm text-ink">{date(limits.endsUtc)}</dd>
            </div>
          </>
        )}
      </dl>
      {limits.isBlocked && <div className="mt-3 text-sm text-alarm">{t(limits.isExhausted ? "trafficLimits.quotaBlocked" : "trafficLimits.blocked")}</div>}
      {limits.isThrottled && <div className="mt-3 text-sm text-warn">{t("trafficLimits.throttled")}</div>}
      {!limits.applied && <div className="mt-3 text-sm text-warn">{t("trafficLimits.notApplied")}</div>}
      {limits.error.length > 0 && <div className="mt-2 break-words text-sm text-alarm">{limits.error}</div>}
      {!asking && reset.error !== null && <div className="mt-3 text-sm text-alarm">{failed}</div>}
      {asking && (
        <Dialog
          title={t("trafficLimits.resetTitle", { name: one.name })}
          onClose={close}
          actions={
            <>
              <button type="button" className={secondary} disabled={reset.isPending} onClick={close}>
                {t("clients.cancel")}
              </button>
              <button
                type="button"
                className={danger}
                disabled={reset.isPending || !editable || !metered}
                onClick={() => reset.mutate(one.id, { onSuccess: () => setAsking(false) })}
              >
                {t("trafficLimits.reset")}
              </button>
            </>
          }
        >
          {reset.error !== null ? <span className="text-alarm">{failed}</span> : undefined}
        </Dialog>
      )}
    </div>
  )
}
