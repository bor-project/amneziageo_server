import { Link } from "react-router-dom"
import { useServices } from "@/api/overview"
import { scopes } from "@/api/scopes"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function ServicesAlert() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const report = useServices(holds(user, scopes.readState)).data
  const down = report?.services.filter((one) => one.faults.length > 0).length ?? 0

  if (down === 0) {
    return null
  }

  const told = t("services.alert", { count: down })

  return (
    <Link
      to="/"
      title={told}
      aria-label={told}
      className="flex items-center gap-1.5 rounded-full border border-alarm-line bg-alarm-soft px-2.5 py-1 text-xs font-medium text-alarm"
    >
      <Mark />
      <span className="max-sm:hidden">{told}</span>
      <span className="sm:hidden">{down}</span>
    </Link>
  )
}

function Mark() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.8">
      <circle cx="12" cy="12" r="8.5" />
      <path d="M12 7.5v5.5M12 16.5v.01" strokeLinecap="round" />
    </svg>
  )
}
