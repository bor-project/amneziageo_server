import { Navigate, Outlet } from "react-router-dom"
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

  return holds(user, scope) ? (
    <Outlet />
  ) : (
    <div className="rounded border border-line bg-surface p-6 text-sm text-muted">{t("access.denied")}</div>
  )
}
