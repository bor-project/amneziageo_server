import { useEffect, useState } from "react"
import { scopes } from "@/api/scopes"
import { busy, useUpdate } from "@/api/update"
import { card, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppDispatch, useAppSelector } from "@/store/hooks"
import { updateClosed } from "@/store/uiSlice"

type Mark = "done" | "now" | "wait" | "fail"

const steps: TextKey[] = ["update.stepLoad", "update.stepInstall", "update.stepRestart", "update.stepDone"]

const notes: TextKey[] = ["update.noteLoad", "update.noteInstall", "update.noteRestart"]

const tones: Record<Mark, string> = { done: "text-muted", now: "text-ink", wait: "text-faint", fail: "text-alarm" }

// How long the panel may stay silent before the window lets itself be closed.
const patience = 180000

export function UpdateWatch() {
  const t = useText()
  const dispatch = useAppDispatch()
  const user = useAppSelector((s) => s.auth.user)
  const watch = useAppSelector((s) => s.ui.updateWatch)
  const hidden = useAppSelector((s) => s.ui.updateHidden)
  const update = useUpdate(holds(user, scopes.readState))
  const [now, setNow] = useState(() => Date.now())
  const state = update.data
  const to = watch?.to ?? state?.run?.to ?? state?.latest?.version ?? ""
  const lost = update.isError
  const arrived = !lost && state !== undefined && to !== "" && state.current === to
  const failed = !lost && state?.run?.state === "failed" && state.run.to === to
  const shown = to !== "" && hidden !== to && (watch !== null || busy(state))
  const silent = lost && now - update.dataUpdatedAt > patience
  const installed = (state?.run?.log ?? []).some((line) => line.includes("starting ") || line.includes("handing "))
  const at = arrived ? steps.length : lost ? 2 : state?.stage === "downloading" ? 0 : 1
  const moving = !failed && !arrived && !silent

  useEffect(() => {
    if (!shown) {
      return
    }

    const timer = window.setInterval(() => setNow(Date.now()), 1000)

    return () => window.clearInterval(timer)
  }, [shown])

  useEffect(() => {
    if (!shown || !arrived) {
      return
    }

    const timer = window.setTimeout(() => window.location.reload(), 2000)

    return () => window.clearTimeout(timer)
  }, [shown, arrived])

  if (!shown) {
    return null
  }

  function mark(step: number): Mark {
    if (failed) {
      const broke = installed ? 1 : 0

      return step < broke ? "done" : step === broke ? "fail" : "wait"
    }

    if (step < at) {
      return "done"
    }

    return step === at ? "now" : "wait"
  }

  function note(): string {
    if (failed) {
      return t("update.noteFailed", { version: state?.current ?? "" })
    }

    if (arrived) {
      return t("update.noteDone", { version: to })
    }

    if (silent) {
      return t("update.noteSilent", { minutes: Math.floor((now - update.dataUpdatedAt) / 60000) })
    }

    const told = t(notes[at], { version: to })

    return watch === null ? told : `${told}, ${clock(now - watch.started)}`
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/45 p-4">
      <div role="dialog" aria-modal="true" aria-labelledby="update-watch" className={`w-full max-w-[22rem] p-5 ${card}`}>
        <div id="update-watch" className="text-[15px] font-semibold text-ink">
          {t("update.watchTitle", { version: to })}
        </div>

        <ol className="mt-3 flex flex-col gap-2 text-sm">
          {steps.map((step, i) => (
            <li key={step} className={`flex items-center gap-2 ${tones[mark(i)]}`}>
              <Sign mark={mark(i)} />
              {t(step)}
            </li>
          ))}
        </ol>

        <div className={`relative mt-4 h-[3px] overflow-hidden rounded-full bg-line ${moving ? "" : "invisible"}`}>
          <span className="absolute inset-y-0 w-[35%] animate-slide rounded-full bg-brand" />
        </div>

        <div className={`mt-3 text-[13px] ${failed ? "text-alarm" : arrived ? "text-good" : "text-muted"}`}>{note()}</div>

        {failed && state?.run !== null && state?.run !== undefined && state.run.log.length > 0 && (
          <details className="mt-2 text-[13px]">
            <summary className="cursor-pointer text-muted">{t("update.log")}</summary>
            <pre className="mt-2 max-h-48 overflow-auto text-xs whitespace-pre-wrap text-muted">
              {state.run.log.join("\n")}
            </pre>
          </details>
        )}

        {(failed || silent) && (
          <div className="mt-4 flex justify-end">
            <button type="button" onClick={() => dispatch(updateClosed(to))} className={secondary}>
              {t("update.close")}
            </button>
          </div>
        )}
      </div>
    </div>
  )
}

function Sign({ mark }: { mark: Mark }) {
  if (mark === "done") {
    return (
      <svg viewBox="0 0 24 24" className="size-4 shrink-0 text-good" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden>
        <path d="M5 12.5l4.5 4.5L19 7.5" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    )
  }

  if (mark === "now") {
    return (
      <svg viewBox="0 0 24 24" className="size-4 shrink-0 animate-spin text-brand" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden>
        <path d="M12 3a9 9 0 1 1-9 9" strokeLinecap="round" />
      </svg>
    )
  }

  if (mark === "fail") {
    return (
      <svg viewBox="0 0 24 24" className="size-4 shrink-0 text-alarm" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden>
        <path d="M7 7l10 10M17 7L7 17" strokeLinecap="round" />
      </svg>
    )
  }

  return (
    <svg viewBox="0 0 24 24" className="size-4 shrink-0" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden>
      <circle cx="12" cy="12" r="8" />
    </svg>
  )
}

function clock(span: number): string {
  const seconds = Math.max(0, Math.floor(span / 1000))

  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`
}
