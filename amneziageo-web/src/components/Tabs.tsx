import { Link, NavLink, Outlet, useLocation } from "react-router-dom"
import { useCrumbs } from "@/components/crumbs"
import type { Crumb } from "@/components/crumbs"
import { primary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export interface Tab {
  to: string
  label: TextKey
  scope: string
  end?: boolean
  add?: { to: string; scope: string }
}

const tab = "-mb-px shrink-0 border-b-2 px-3.5 py-2.5 text-sm"
const chosen = "border-brand font-medium text-ink"
const plain = "border-transparent text-muted hover:text-ink"

export function Tabbed({ title, tabs }: { title: TextKey; tabs: Tab[] }) {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const { pathname } = useLocation()
  const open = tabs.filter((one) => holds(user, one.scope))
  const now = here(open, pathname)
  const add = now?.add !== undefined && holds(user, now.add.scope) ? now.add.to : null

  useCrumbs(trail(t, title, open, now))

  return (
    <div>
      <div className="flex items-start justify-between gap-4">
        <h1 className="text-2xl leading-10 font-semibold tracking-[-0.02em]">{t(title)}</h1>
        {add !== null && (
          <Link to={add} className={`flex h-10 shrink-0 items-center ${primary}`}>
            {t("action.add")}
          </Link>
        )}
      </div>

      <div className="mt-4 flex gap-4 overflow-x-auto overflow-y-hidden border-b border-line [scrollbar-width:none] sm:gap-6">
        {open.map((one) => (
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

function here(tabs: Tab[], pathname: string): Tab | undefined {
  return tabs.find((one) => (one.end === true ? pathname === one.to : pathname.startsWith(one.to)))
}

function trail(t: Text, title: TextKey, tabs: Tab[], open: Tab | undefined): Crumb[] {
  const root = tabs.find((one) => one.end === true)?.to ?? tabs[0]?.to

  if (open === undefined) {
    return root === undefined ? [{ label: t(title) }] : [{ label: t(title), to: root }]
  }

  return [
    { label: t(title), to: root },
    {
      label: t(open.label),
      to: open.to,
      options: tabs.map((one) => ({ label: t(one.label), to: one.to, mark: one.to === open.to })),
    },
  ]
}
