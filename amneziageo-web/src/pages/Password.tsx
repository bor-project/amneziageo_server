import { useState } from "react"
import type { FormEvent } from "react"
import { Navigate, useNavigate } from "react-router-dom"
import { changePassword, complaint } from "@/api/auth"
import { LanguagePicker } from "@/components/LanguagePicker"
import { ThemeToggle } from "@/components/ThemeToggle"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { useAppDispatch, useAppSelector } from "@/store/hooks"
import { sessionOpened } from "@/store/authSlice"

export function Password() {
  const t = useText()
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const status = useAppSelector((s) => s.auth.status)
  const user = useAppSelector((s) => s.auth.user)
  const [current, setCurrent] = useState("")
  const [next, setNext] = useState("")
  const [again, setAgain] = useState("")
  const [error, setError] = useState<TextKey | null>(null)
  const [busy, setBusy] = useState(false)

  if (status === "anonymous" || status === "unknown") {
    return <Navigate to="/login" replace />
  }

  const mismatch = again.length > 0 && next !== again
  const ready = current.length > 0 && next.length > 0 && !mismatch

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      dispatch(sessionOpened(await changePassword(current, next)))
      navigate("/", { replace: true })
    } catch (failure) {
      setError(complaint(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex h-full items-center justify-center bg-canvas px-4">
      <div className="w-full max-w-sm">
        <div className="mb-3 flex items-center justify-end gap-2">
          <ThemeToggle />
          <LanguagePicker />
        </div>

        <form onSubmit={submit} className="rounded-lg border border-line bg-surface p-6 shadow-sm">
          <div className="text-lg font-semibold text-brand-ink">{t("password.title")}</div>
          <p className="mt-1 text-sm text-muted">
            {status === "must-change"
              ? t("password.issued")
              : t("password.account", { name: user?.name ?? "" })}
          </p>

          <label className="mt-6 block text-sm text-muted" htmlFor="current">
            {t("password.current")}
          </label>
          <input
            id="current"
            type="password"
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
            autoComplete="current-password"
            autoFocus
            className="mt-1 w-full rounded border border-line bg-surface px-3 py-2 text-sm text-ink outline-none focus:border-brand"
          />

          <label className="mt-4 block text-sm text-muted" htmlFor="next">
            {t("password.next")}
          </label>
          <input
            id="next"
            type="password"
            value={next}
            onChange={(e) => setNext(e.target.value)}
            autoComplete="new-password"
            className="mt-1 w-full rounded border border-line bg-surface px-3 py-2 text-sm text-ink outline-none focus:border-brand"
          />

          <label className="mt-4 block text-sm text-muted" htmlFor="again">
            {t("password.again")}
          </label>
          <input
            id="again"
            type="password"
            value={again}
            onChange={(e) => setAgain(e.target.value)}
            autoComplete="new-password"
            className="mt-1 w-full rounded border border-line bg-surface px-3 py-2 text-sm text-ink outline-none focus:border-brand"
          />

          {mismatch && <div className="mt-4 text-sm text-alarm">{t("password.mismatch")}</div>}
          {error && (
            <div className="mt-4 rounded bg-alarm-soft px-3 py-2 text-sm text-alarm">{t(error)}</div>
          )}

          <button
            type="submit"
            disabled={busy || !ready}
            className="mt-6 w-full rounded bg-brand px-3 py-2 text-sm font-medium text-white disabled:opacity-50"
          >
            {busy ? t("password.busy") : t("password.submit")}
          </button>
        </form>
      </div>
    </div>
  )
}
