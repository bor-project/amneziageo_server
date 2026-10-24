import { useState } from "react"
import type { ReactNode } from "react"
import { useNavigate } from "react-router-dom"
import { signOut } from "@/api/auth"
import { queryClient } from "@/api/queryClient"
import { scopes } from "@/api/scopes"
import { useAcceptTerms, useTerms } from "@/api/terms"
import { LegalBody } from "@/components/LegalBody"
import { primary, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import { legalText } from "@/legal"
import type { LegalName } from "@/legal"
import { holds, sessionClosed } from "@/store/authSlice"
import { useAppDispatch, useAppSelector } from "@/store/hooks"

const page = "flex min-h-full items-start justify-center bg-canvas px-4 py-8"
const sheet = "flex w-full flex-col gap-3 rounded-lg border border-line bg-surface p-6 shadow-sm"

// Keeps the panel behind its terms of use until an account that manages access accepts the version it asks for.
export function TermsGate({ children }: { children: ReactNode }) {
  const terms = useTerms()

  if (terms.isPending) {
    return null
  }

  if (terms.isError || terms.data.accepted) {
    return children
  }

  return <Terms version={terms.data.version} />
}

function Terms({ version }: { version: number }) {
  const t = useText()
  const language = useLanguage()
  const user = useAppSelector((s) => s.auth.user)
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const accept = useAcceptTerms()
  const [name, setName] = useState<LegalName>("terms")
  const [reading, setReading] = useState(false)
  const allowed = holds(user, scopes.manageAccess)
  const text = legalText(name, language)

  async function leave() {
    await signOut()
    queryClient.clear()
    dispatch(sessionClosed())
    navigate("/login", { replace: true })
  }

  const quit = (
    <button type="button" onClick={() => void leave()} className={secondary}>
      {t("terms.quit")}
    </button>
  )

  if (!allowed && !reading) {
    return (
      <div className={page}>
        <div className={`${sheet} max-w-md`}>
          <div className="text-lg font-semibold text-ink">{t("terms.waitTitle")}</div>
          <p className="text-sm text-muted">{t("terms.waitText")}</p>
          <button type="button" onClick={() => setReading(true)} className="self-start text-sm text-brand hover:underline">
            {t("terms.read")}
          </button>
          <div className="flex justify-end">{quit}</div>
        </div>
      </div>
    )
  }

  return (
    <div className={page}>
      <div className={`${sheet} max-w-2xl`}>
        <div className="text-lg font-semibold text-ink">{t("terms.title")}</div>
        <p className="text-sm text-muted">{allowed ? t("terms.hint") : t("terms.waitText")}</p>

        <div className="grid grid-cols-2 gap-1 rounded-lg bg-canvas p-1" role="tablist">
          {(["terms", "privacy"] as const).map((one) => (
            <button
              key={one}
              type="button"
              role="tab"
              aria-selected={name === one}
              onClick={() => setName(one)}
              className={`rounded-md px-3 py-1.5 text-sm ${name === one ? "bg-surface font-medium text-ink shadow-sm" : "text-muted hover:text-ink"}`}
            >
              {t(one === "terms" ? "terms.tabTerms" : "terms.tabPrivacy")}
            </button>
          ))}
        </div>

        <div tabIndex={0} className="max-h-[55vh] overflow-y-auto rounded-lg border border-line px-4 py-3">
          <LegalBody blocks={text.body} />
        </div>

        {accept.isError && <div className="text-sm text-alarm">{t("error.failed")}</div>}

        <div className="flex flex-wrap items-center gap-2">
          <span className="min-w-40 flex-1 text-xs text-muted">{`${text.version} · GPL-3.0`}</span>
          {quit}
          {allowed && (
            <button type="button" disabled={accept.isPending} onClick={() => accept.mutate(version)} className={primary}>
              {t("terms.accept")}
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
