import { useState } from "react"
import type { FormEvent } from "react"
import axios from "axios"
import { Navigate, useNavigate } from "react-router-dom"
import { complaint } from "@/api/auth"
import { LanguagePicker } from "@/components/LanguagePicker"
import { ThemeToggle } from "@/components/ThemeToggle"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { http, version } from "./http"
import { keepToken, useToken } from "./session"

export function TokenLogin() {
  const t = useText()
  const navigate = useNavigate()
  const signed = useToken()
  const [typed, setTyped] = useState("")
  const [error, setError] = useState<TextKey | null>(null)
  const [busy, setBusy] = useState(false)

  if (signed !== null) {
    return <Navigate to="/" replace />
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    const held = typed.trim()
    setBusy(true)
    setError(null)
    try {
      await http.get("/status", { headers: { Authorization: `Bearer ${held}` } })
      keepToken(held)
      navigate("/", { replace: true })
    } catch (failure) {
      setError(axios.isAxiosError(failure) && failure.response?.status === 401 ? "updates.badToken" : complaint(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex h-full items-center justify-center bg-canvas px-4">
      <form onSubmit={submit} className="w-full max-w-sm rounded-lg border border-line bg-surface p-6 shadow-sm">
        <div className="text-lg font-semibold text-brand-ink">{t("app.name")}</div>
        <p className="mt-1 text-sm text-muted">{t("updates.title")}</p>

        <label className="mt-6 block text-sm text-muted" htmlFor="token">
          {t("clientUpdates.token")}
        </label>
        <input
          id="token"
          type="password"
          value={typed}
          onChange={(e) => setTyped(e.target.value)}
          autoComplete="current-password"
          autoFocus
          className="mt-1 w-full rounded border border-line bg-surface px-3 py-2 font-mono text-sm text-ink outline-none focus:border-brand"
        />

        {error && <div className="mt-4 rounded bg-alarm-soft px-3 py-2 text-sm text-alarm">{t(error)}</div>}

        <button
          type="submit"
          disabled={busy || typed.trim().length === 0}
          className="mt-6 w-full rounded bg-brand px-3 py-2 text-sm font-medium text-white disabled:opacity-50"
        >
          {busy ? t("login.busy") : t("login.submit")}
        </button>

        <div className="mt-4 flex items-center justify-between gap-2">
          <span className="text-xs text-muted">{version.length > 0 ? t("updates.version", { version }) : ""}</span>
          <div className="flex items-center gap-2">
            <LanguagePicker />
            <ThemeToggle />
          </div>
        </div>
      </form>
    </div>
  )
}
