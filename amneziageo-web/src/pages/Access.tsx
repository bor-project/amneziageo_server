import { NavLink, Outlet } from "react-router-dom"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

const tabs: { to: string; label: TextKey; end?: boolean }[] = [
  { to: "/settings/access", label: "users.title", end: true },
  { to: "/settings/access/roles", label: "roles.title" },
]

const tab = "-mb-px border-b-2 px-1 pb-2 text-sm"
const chosen = "border-brand font-medium text-brand-ink"
const plain = "border-transparent text-muted hover:text-brand-ink"

export function Access() {
  const t = useText()

  return (
    <div>
      <h1 className="text-xl font-semibold">{t("nav.access")}</h1>

      <div className="mt-4 flex gap-6 border-b border-line">
        {tabs.map((one) => (
          <NavLink
            key={one.to}
            to={one.to}
            end={one.end}
            className={({ isActive }) => `${tab} ${isActive ? chosen : plain}`}
          >
            {t(one.label)}
          </NavLink>
        ))}
      </div>

      <Outlet />
    </div>
  )
}
