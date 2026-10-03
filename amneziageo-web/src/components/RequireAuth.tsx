import { Navigate, Outlet, useOutletContext } from "react-router-dom"
import { useText } from "@/i18n"
import { useAppSelector } from "@/store/hooks"
import { holds } from "@/store/authSlice"

export function RequireAuth() {
  const status = useAppSelector((s) => s.auth.status)

  if (status === "must-change") {
    return <Navigate to="/password" replace />
  }

  return status === "active" ? <Outlet /> : <Navigate to="/login" replace />
}

export function RequireScope({ scope }: { scope: string }) {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const context = useOutletContext()

  return holds(user, scope) ? (
    <Outlet context={context} />
  ) : (
    <div className="rounded border border-line bg-surface p-6 text-sm text-muted">{t("access.denied")}</div>
  )
}
