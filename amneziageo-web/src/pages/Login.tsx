import { useState } from "react"
import type { FormEvent } from "react"
import { Navigate } from "react-router-dom"
import { complaint, signIn } from "@/api/auth"
import { useHealth } from "@/api/health"
import { LanguagePicker } from "@/components/LanguagePicker"
import { ThemeToggle } from "@/components/ThemeToggle"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { useAppDispatch, useAppSelector } from "@/store/hooks"
import { sessionOpened } from "@/store/authSlice"

export function Login() {
  const t = useText()
  const dispatch = useAppDispatch()
  const status = useAppSelector((s) => s.auth.status)
  const health = useHealth()
  const [user, setUser] = useState("")
  const [password, setPassword] = useState("")
  const [error, setError] = useState<TextKey | null>(null)
  const [busy, setBusy] = useState(false)

  if (status === "active") {
    return <Navigate to="/" replace />
  }

  if (status === "must-change") {
    return <Navigate to="/password" replace />
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      dispatch(sessionOpened(await signIn(user.trim(), password)))
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
          <div className="text-lg font-semibold text-brand-ink">{t("app.name")}</div>
          <p className="mt-1 text-sm text-muted">{t("app.subtitle")}</p>

          <label className="mt-6 block text-sm text-muted" htmlFor="user">
            {t("login.user")}
          </label>
          <input
            id="user"
            value={user}
            onChange={(e) => setUser(e.target.value)}
            autoComplete="username"
            autoFocus
            className="mt-1 w-full rounded border border-line bg-surface px-3 py-2 text-sm text-ink outline-none focus:border-brand"
          />

          <label className="mt-4 block text-sm text-muted" htmlFor="password">
            {t("login.password")}
          </label>
          <input
            id="password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            className="mt-1 w-full rounded border border-line bg-surface px-3 py-2 text-sm text-ink outline-none focus:border-brand"
          />

          {error && (
            <div className="mt-4 rounded bg-alarm-soft px-3 py-2 text-sm text-alarm">{t(error)}</div>
          )}

          <button
            type="submit"
            disabled={busy || user.length === 0 || password.length === 0}
            className="mt-6 w-full rounded bg-brand px-3 py-2 text-sm font-medium text-white disabled:opacity-50"
          >
            {busy ? t("login.busy") : t("login.submit")}
          </button>

          <p className="mt-4 text-center text-xs text-muted">
            {health.isError
              ? t("health.silent")
              : t("health.version", { version: health.data?.version ?? "" })}
          </p>
        </form>
      </div>
    </div>
  )
}
