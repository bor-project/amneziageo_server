import { useState } from "react"
import { NavLink, Outlet, useNavigate } from "react-router-dom"
import { signOut } from "@/api/auth"
import { useHealth } from "@/api/health"
import { scopes } from "@/api/scopes"
import { queryClient } from "@/api/queryClient"
import { LanguagePicker } from "@/components/LanguagePicker"
import { PasswordDialog } from "@/components/PasswordDialog"
import { ThemeToggle } from "@/components/ThemeToggle"
import { card } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { useAppDispatch, useAppSelector } from "@/store/hooks"
import { holds, sessionClosed } from "@/store/authSlice"
import { sidebarToggled } from "@/store/uiSlice"

const links: { to: string; label: TextKey; scope: string }[] = [
  { to: "/", label: "nav.overview", scope: scopes.readState },
  { to: "/configs", label: "nav.configs", scope: scopes.readState },
  { to: "/clients", label: "nav.clients", scope: scopes.readState },
  { to: "/geo", label: "nav.geo", scope: scopes.readState },
  { to: "/outbounds", label: "nav.outbounds", scope: scopes.readState },
  { to: "/balancers", label: "nav.balancers", scope: scopes.readState },
  { to: "/rules", label: "nav.rules", scope: scopes.readState },
  { to: "/dns", label: "nav.dns", scope: scopes.readState },
  { to: "/access", label: "nav.access", scope: scopes.manageAccess },
]

const item = "rounded px-3 py-2 text-sm"
const active = "bg-brand-soft font-medium text-brand-ink"
const idle = "text-muted hover:bg-hover"

export function Layout() {
  const t = useText()
  const open = useAppSelector((s) => s.ui.sidebarOpen)
  const user = useAppSelector((s) => s.auth.user)
  const dispatch = useAppDispatch()
  const health = useHealth()

  return (
    <div className="flex h-full bg-canvas text-ink">
      {open && (
        <aside className="flex w-56 shrink-0 flex-col border-r border-line bg-surface">
          <div className="flex h-13 items-center px-4">
            <span className="text-lg font-semibold text-brand-ink">{t("app.name")}</span>
          </div>

          <nav className="flex flex-1 flex-col gap-1 px-2 pt-2">
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
          </nav>

          <div className="border-t border-line px-4 pt-2 pb-4 text-xs text-muted">{state(t, health)}</div>
        </aside>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-13 items-center gap-2 border-b border-line bg-surface px-3">
          <button
            type="button"
            title={t("layout.nav")}
            aria-label={t("layout.nav")}
            onClick={() => dispatch(sidebarToggled())}
            className="rounded p-2 text-muted hover:bg-hover hover:text-brand-ink"
          >
            <Bars />
          </button>
          <div className="ml-auto flex items-center gap-2">
            <ThemeToggle />
            <UserMenu />
          </div>
        </header>

        <main className="min-h-0 flex-1 overflow-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}

function UserMenu() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [changing, setChanging] = useState(false)

  async function leave() {
    setOpen(false)
    await signOut()
    queryClient.clear()
    dispatch(sessionClosed())
    navigate("/login", { replace: true })
  }

  const name = user === null ? "" : user.displayName.length > 0 ? user.displayName : user.name

  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="flex items-center gap-2 rounded px-1.5 py-1 text-sm text-ink hover:bg-hover"
      >
        <span className="flex size-7 items-center justify-center rounded-full bg-brand-soft text-xs font-medium text-brand-ink">
          {name.slice(0, 1).toUpperCase()}
        </span>
        <span className="hidden max-w-40 truncate sm:block">{name}</span>
        <Chevron open={open} />
      </button>

      {open && (
        <>
          <div className="fixed inset-0 z-30" onMouseDown={() => setOpen(false)} />
          <div className={`absolute right-0 z-40 mt-1 w-56 py-1 shadow-lg ${card}`}>
            <div className="border-b border-line px-3 pt-1 pb-2">
              <div className="truncate text-sm text-ink">{name}</div>
              <div className="truncate text-xs text-muted">{user === null ? "" : user.role}</div>
            </div>

            <div className="border-b border-line px-3 py-2">
              <LanguagePicker className="w-full" />
            </div>

            <button
              type="button"
              onClick={() => {
                setOpen(false)
                setChanging(true)
              }}
              className="w-full px-3 py-2 text-left text-sm text-muted hover:bg-hover hover:text-brand-ink"
            >
              {t("password.title")}
            </button>

            <button
              type="button"
              onClick={() => void leave()}
              className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-muted hover:bg-hover hover:text-brand-ink"
            >
              <Door />
              {t("layout.signOut")}
            </button>
          </div>
        </>
      )}

      {changing && <PasswordDialog onClose={() => setChanging(false)} />}
    </div>
  )
}

function state(t: Text, health: ReturnType<typeof useHealth>): string {
  if (health.isError) {
    return t("health.silent")
  }

  return health.data ? t("health.version", { version: health.data.version }) : t("health.checking")
}

function Bars() {
  return (
    <svg viewBox="0 0 24 24" className="size-5" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d="M4 7h16M4 12h16M4 17h16" strokeLinecap="round" />
    </svg>
  )
}

function Chevron({ open }: { open: boolean }) {
  return (
    <svg
      viewBox="0 0 24 24"
      className={`size-4 text-muted ${open ? "-rotate-90" : "rotate-90"}`}
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
