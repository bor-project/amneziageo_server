import { useState } from "react"
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom"
import { signOut } from "@/api/auth"
import { useHealth } from "@/api/health"
import { scopes } from "@/api/scopes"
import { queryClient } from "@/api/queryClient"
import { ThemeToggle } from "@/components/ThemeToggle"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { useAppDispatch, useAppSelector } from "@/store/hooks"
import { holds, sessionClosed } from "@/store/authSlice"
import { sidebarToggled } from "@/store/uiSlice"

const links: { to: string; label: TextKey; scope: string }[] = [
  { to: "/", label: "nav.overview", scope: scopes.readState },
  { to: "/interfaces", label: "nav.interfaces", scope: scopes.readState },
  { to: "/clients", label: "nav.clients", scope: scopes.readState },
]

const settings: { to: string; label: TextKey; scope?: string }[] = [
  { to: "/settings/general", label: "nav.general" },
  { to: "/settings/access", label: "nav.access", scope: scopes.manageAccess },
]

const item = "rounded px-3 py-2 text-sm"
const active = "bg-brand-soft font-medium text-brand-ink"
const idle = "text-muted hover:bg-hover"

export function Layout() {
  const t = useText()
  const open = useAppSelector((s) => s.ui.sidebarOpen)
  const user = useAppSelector((s) => s.auth.user)
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const location = useLocation()
  const health = useHealth()
  const [settingsOpen, setSettingsOpen] = useState(location.pathname.startsWith("/settings"))

  async function leave() {
    await signOut()
    queryClient.clear()
    dispatch(sessionClosed())
    navigate("/login", { replace: true })
  }

  return (
    <div className="flex h-full bg-canvas text-ink">
      {open && (
        <aside className="flex w-56 shrink-0 flex-col border-r border-line bg-surface">
          <div className="flex items-center gap-2 px-4 py-4">
            <span className="text-lg font-semibold text-brand-ink">{t("app.name")}</span>
            <ThemeToggle />
          </div>

          <nav className="flex flex-1 flex-col gap-1 px-2">
            {links
              .filter((l) => holds(user, l.scope))
              .map((l) => (
                <NavLink
                  key={l.to}
                  to={l.to}
                  end={l.to === "/"}
                  className={({ isActive }) => `${item} ${isActive ? active : idle}`}
                >
                  {t(l.label)}
                </NavLink>
              ))}

            <button
              type="button"
              onClick={() => setSettingsOpen(!settingsOpen)}
              className={`flex items-center justify-between ${item} ${idle}`}
            >
              {t("nav.settings")}
              <Chevron open={settingsOpen} />
            </button>
            {settingsOpen &&
              settings
                .filter((l) => l.scope === undefined || holds(user, l.scope))
                .map((l) => (
                  <NavLink
                    key={l.to}
                    to={l.to}
                    className={({ isActive }) => `${item} ml-3 ${isActive ? active : idle}`}
                  >
                    {t(l.label)}
                  </NavLink>
                ))}
          </nav>

          <button
            type="button"
            onClick={() => void leave()}
            className="flex items-center gap-2 border-t border-line px-4 py-3 text-sm text-muted hover:bg-hover hover:text-brand-ink"
          >
            <Door />
            {t("layout.signOut")}
          </button>

          <div className="border-t border-line px-4 pt-2 pb-5 text-xs text-muted">{state(t, health)}</div>
        </aside>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex items-center gap-4 border-b border-line bg-surface px-4 py-3">
          <button
            type="button"
            onClick={() => dispatch(sidebarToggled())}
            className="rounded border border-line px-2 py-1 text-sm text-muted hover:bg-hover"
          >
            {t("layout.menu")}
          </button>
          <div className="ml-auto flex items-center gap-3">
            {!open && <ThemeToggle />}
          </div>
        </header>

        <main className="min-h-0 flex-1 overflow-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}

function state(t: Text, health: ReturnType<typeof useHealth>): string {
  if (health.isError) {
    return t("health.silent")
  }

  return health.data ? t("health.version", { version: health.data.version }) : t("health.checking")
}

function Chevron({ open }: { open: boolean }) {
  return (
    <svg
      viewBox="0 0 24 24"
      className={`size-4 ${open ? "rotate-90" : ""}`}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
    >
      <path d="m9 6 6 6-6 6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

function Door() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path
        d="M15 4h3a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-3M10 8l-4 4 4 4M6 12h9"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}
