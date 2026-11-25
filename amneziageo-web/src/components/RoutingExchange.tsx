import { useRef, useState } from "react"
import axios from "axios"
import { complaint } from "@/api/auth"
import { useRoutingApply, useRoutingExport, useRoutingImport } from "@/api/exchange"
import type { ExchangeAnswer, ExchangePlan } from "@/api/exchange"
import { Glyph } from "@/components/Glyph"
import { Help } from "@/components/fields"
import { card, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text } from "@/i18n"
export function RoutingExchange({ may }: { may: boolean }) {
  const t = useText()
  const take = useRoutingExport()
  const read = useRoutingImport()
  const put = useRoutingApply()
  const picker = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [plan, setPlan] = useState<ExchangePlan | null>(null)
  const failed = take.error ?? read.error ?? put.error

  function go(picked: File) {
    setFile(picked)
    setPlan(null)
    put.reset()
    take.reset()
    void read
      .mutateAsync({ file: picked })
      .then(setPlan)
      .catch(() => undefined)
  }

  function reset() {
    setFile(null)
    setPlan(null)
    read.reset()
    put.reset()
  }

  return (
    <div className={`mt-4 flex flex-col gap-3 p-4 ${card}`}>
      <div className="flex items-center gap-2 text-sm font-medium text-ink">
        <Glyph name="fork" />
        {t("exchange.title")}
        <Help text={t("exchange.about")} />
      </div>

      {put.isSuccess ? (
        <>
          <div className="text-sm font-semibold text-ink">{t("exchange.done")}</div>
          <Answer answer={put.data} t={t} />
          <div className="flex flex-wrap gap-2">
            <button type="button" className={secondary} onClick={reset}>
              {t("exchange.close")}
            </button>
          </div>
        </>
      ) : plan !== null && file !== null ? (
        <>
          <div className="text-sm font-semibold text-ink">{t("exchange.plan")}</div>
          <div className="flex flex-wrap items-baseline gap-x-3 text-sm">
            <span className="truncate text-ink" title={file.name}>
              {file.name}
            </span>
            <span className="text-muted">
              {t("exchange.file", {
                rules: plan.doc.rules.length,
                entries: plan.doc.entries.length,
                presets: plan.doc.presets.length,
                sources: plan.doc.sources.length,
              })}
            </span>
          </div>
          <Plan plan={plan} t={t} />
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              className={primary}
              disabled={put.isPending}
              onClick={() => void put.mutateAsync({ file }).catch(() => undefined)}
            >
              {put.isPending ? t("exchange.busy") : t("exchange.apply")}
            </button>
            <button type="button" className={secondary} disabled={put.isPending} onClick={reset}>
              {t("exchange.cancel")}
            </button>
          </div>
        </>
      ) : (
        <div className="flex flex-wrap gap-2">
          <button type="button" className={primary} disabled={take.isPending} onClick={() => take.mutate()}>
            {take.isPending ? t("exchange.busy") : t("exchange.export")}
          </button>
          {may && (
            <button type="button" className={secondary} disabled={read.isPending} onClick={() => picker.current?.click()}>
              {t("exchange.pick")}
            </button>
          )}
        </div>
      )}

      <input
        ref={picker}
        type="file"
        accept=".json,application/json"
        className="hidden"
        onChange={(event) => {
          const picked = event.target.files?.[0]
          event.target.value = ""
          if (picked !== undefined) {
            go(picked)
          }
        }}
      />

      {failed !== null && (
        <div className="text-sm text-alarm">
          {t(complaint(failed))}
          {said(failed).length > 0 && <div className="mt-0.5 text-xs text-muted">{said(failed)}</div>}
        </div>
      )}
    </div>
  )
}

function Plan({ plan, t }: { plan: ExchangePlan; t: Text }) {
  const added = [
    ...plan.newEntries.map((name) => t("routing.entryItem", { name })),
    ...plan.newRules.map((name) => t("exchange.itemRule", { name })),
    ...plan.newPresets.map((name) => t("exchange.itemPreset", { name })),
    ...plan.newSources.map((name) => t("exchange.itemSource", { name })),
  ]
  const replaced = [
    ...plan.replacedEntries.map((name) => t("routing.entryItem", { name })),
    ...plan.replacedRules.map((name) => t("exchange.itemRule", { name })),
    ...plan.replacedPresets.map((name) => t("exchange.itemPreset", { name })),
    ...plan.replacedSources.map((name) => t("exchange.itemSource", { name })),
  ]

  return (
    <div className="grid gap-4 wide:grid-cols-2">
      <div className="flex flex-col gap-1.5 text-sm">
        <div className="font-semibold text-ink">{t("exchange.added")}</div>
        {added.length === 0 ? (
          <div className="text-muted">{t("exchange.none")}</div>
        ) : (
          added.map((one, at) => (
            <div key={at}>{one}</div>
          ))
        )}
        <div className="mt-2 font-semibold text-ink">{t("exchange.replaced")}</div>
        {replaced.length === 0 ? (
          <div className="text-muted">{t("exchange.none")}</div>
        ) : (
          replaced.map((one, at) => (
            <div key={at}>{one}</div>
          ))
        )}
      </div>
      <div className="flex flex-col gap-1.5 text-sm">
        <div className="font-semibold text-ink">{t("exchange.rest")}</div>
        <div className="text-muted">{t("exchange.restNote")}</div>
        {plan.detached.length > 0 && (
          <div className="mt-2 rounded-lg bg-warn-soft px-3 py-2 text-[13px] text-warn">
            <div className="font-semibold">{t("exchange.detached")}</div>
            {plan.detached.map((one) => (
              <div key={one}>{one}</div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

function Answer({ answer, t }: { answer: ExchangeAnswer; t: Text }) {
  return (
    <div className="flex flex-col gap-2 text-sm">
      <AnswerRow head={t("exchange.took")} lines={answer.added} />
      <AnswerRow head={t("exchange.swapped")} lines={answer.replaced} />
      <AnswerRow head={t("exchange.refused")} lines={answer.refused} alarm />
      <AnswerRow head={t("exchange.still")} lines={answer.detached} />
    </div>
  )
}

function AnswerRow({ head, lines, alarm }: { head: string; lines: string[]; alarm?: boolean }) {
  if (lines.length === 0) {
    return null
  }

  return (
    <div className="flex flex-col gap-0.5">
      <div className={alarm ? "font-semibold text-alarm" : "font-semibold text-ink"}>{head}</div>
      {lines.map((one) => (
        <div key={one} className="text-body">
          {one}
        </div>
      ))}
    </div>
  )
}

function said(error: unknown): string {
  if (!axios.isAxiosError(error)) {
    return ""
  }

  return (error.response?.data as { message?: string } | undefined)?.message ?? ""
}
