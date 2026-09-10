import { NavLink, Outlet } from "react-router-dom"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export interface Tab {
  to: string
  label: TextKey
  scope: string
  end?: boolean
}

const tab = "-mb-px border-b-2 px-1 pb-2 text-sm"
const chosen = "border-brand font-medium text-brand-ink"
const plain = "border-transparent text-muted hover:text-brand-ink"

export function Tabbed({ title, tabs }: { title: TextKey; tabs: Tab[] }) {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)

  return (
    <div>
      <h1 className="text-xl font-semibold">{t(title)}</h1>

      <div className="mt-4 flex gap-6 border-b border-line">
        {tabs
          .filter((one) => holds(user, one.scope))
          .map((one) => (
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
