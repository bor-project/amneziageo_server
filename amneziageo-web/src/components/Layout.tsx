import { useEffect, useState } from "react"
import { Link, NavLink, Outlet, useLocation, useNavigate } from "react-router-dom"
import { signOut } from "@/api/auth"
import { useHealth } from "@/api/health"
import { usePanel } from "@/api/panel"
import { queryClient } from "@/api/queryClient"
import { Crumbs, CrumbsHolder } from "@/components/Crumbs"
import { LanguagePicker } from "@/components/LanguagePicker"
import { RestartButton } from "@/components/RestartButton"
import { ThemeToggle } from "@/components/ThemeToggle"
import { sections, under } from "@/components/menu"
import type { Item, Section } from "@/components/menu"
import { menu, menuItem } from "@/components/styles"
import { isLanguageChoice, useText } from "@/i18n"
import type { Text } from "@/i18n"
import { useAbove, wideQuery } from "@/theme/width"
import { useAppDispatch, useAppSelector } from "@/store/hooks"
import { holds, sessionClosed } from "@/store/authSlice"
import { lastSpot, sectionOf } from "@/store/spots"
import { languageServed, sidebarSet, sidebarToggled } from "@/store/uiSlice"

const item = "rounded-lg px-2.5 py-2 text-sm"
const leaf = "rounded-lg py-1.5 pr-2.5 text-[13px]"
const active = "bg-active font-medium text-ink"
const idle = "text-muted hover:bg-nav hover:text-ink"
const opened = "font-medium text-ink hover:bg-nav"
const column = "flex w-54 shrink-0 flex-col gap-6 border-r border-line bg-chrome px-3 py-5"
const drawer = "fixed inset-y-0 left-0 z-50 flex w-64 flex-col gap-6 border-r border-line bg-chrome px-3 py-5 shadow-xl"

export function Layout() {
  const t = useText()
  const open = useAppSelector((s) => s.ui.sidebarOpen)
  const user = useAppSelector((s) => s.auth.user)
  const dispatch = useAppDispatch()
  const health = useHealth()
  const served = usePanel().data?.language
  const wide = useAbove(wideQuery)

  useEffect(() => {
    if (served !== undefined && isLanguageChoice(served)) {
      dispatch(languageServed(served))
    }
  }, [dispatch, served])

  useEffect(() => {
    dispatch(sidebarSet(wide))
  }, [dispatch, wide])

  function shut() {
    if (!wide) {
      dispatch(sidebarSet(false))
    }
  }

  useEffect(() => {
    const escape = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !wide) {
        dispatch(sidebarSet(false))
      }
    }

    window.addEventListener("keydown", escape)

    return () => window.removeEventListener("keydown", escape)
  }, [dispatch, wide])

  return (
    <CrumbsHolder>
      <div className="flex h-full bg-canvas text-ink">
        {open && !wide && (
          <div className="fixed inset-0 z-40 bg-black/50" onMouseDown={() => dispatch(sidebarSet(false))} />
        )}

        {open && (
          <aside className={wide ? column : drawer}>
            <div className="flex items-center gap-2 px-1">
              <span className="size-5.5 rounded-md bg-brand" aria-hidden />
              <span className="text-sm font-semibold tracking-[-0.01em] text-ink">{t("app.name")}</span>
            </div>

            <nav className="flex flex-1 flex-col gap-1 overflow-y-auto">
              {sections
                .filter((one) => holds(user, one.scope))
                .map((one) => (
                  <Group key={one.to} section={one} shut={shut} />
                ))}
            </nav>

            <div className="border-t border-line px-1 pt-3 text-xs text-faint">{state(t, health)}</div>
          </aside>
        )}

        <div className="flex min-w-0 flex-1 flex-col">
          <header className="flex min-h-14 items-center justify-between gap-3 border-b border-line bg-chrome px-5 py-2">
            <div className="flex min-w-0 items-center gap-2">
              {!wide && (
                <button
                  type="button"
                  title={t("layout.nav")}
                  aria-label={t("layout.nav")}
                  onClick={() => dispatch(sidebarToggled())}
                  className="rounded-lg p-2 text-muted hover:bg-active hover:text-ink"
                >
                  <Bars />
                </button>
              )}
              <Crumbs />
            </div>

            <div className="flex shrink-0 items-center gap-2">
              <RestartButton />
              <ThemeToggle />
              <UserMenu />
            </div>
          </header>

          <main className="min-h-0 flex-1 overflow-auto p-5">
            <div className="mx-auto max-w-[1240px]">
              <Outlet />
            </div>
          </main>
        </div>
      </div>
    </CrumbsHolder>
  )
}

function Group({ section, shut }: { section: Section; shut: () => void }) {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const { pathname } = useLocation()
  const items = section.items.filter((one) => holds(user, one.scope))

  if (items.length === 0) {
    return (
      <NavLink to={section.to} end onClick={shut} className={({ isActive }) => `${item} ${isActive ? active : idle}`}>
        {t(section.label)}
      </NavLink>
    )
  }

  const inside = under(pathname, section.to)

  return (
    <div className="flex flex-col gap-0.5">
      <Link
        to={section.to}
        aria-expanded={inside}
        className={`flex items-center justify-between gap-2 ${item} ${inside ? opened : idle}`}
      >
        {t(section.label)}
        <Caret open={inside} />
      </Link>
      {inside && items.map((one) => <Leaf key={one.to} one={one} depth={1} shut={shut} />)}
    </div>
  )
}

function Leaf({ one, depth, shut }: { one: Item; depth: number; shut: () => void }) {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const { pathname } = useLocation()
  const kids = (one.kids ?? []).filter((kid) => holds(user, kid.scope))
  const spot = lastSpot(sectionOf(one.to), one.to)
  const to = under(spot, one.to) ? spot : one.to
  const indent = depth > 1 ? "pl-9" : "pl-6"

  if (kids.length === 0) {
    return (
      <NavLink to={to} onClick={shut} className={({ isActive }) => `${leaf} ${indent} ${isActive ? active : idle}`}>
        {t(one.label)}
      </NavLink>
    )
  }

  const inside = under(pathname, one.to)

  return (
    <>
      <Link
        to={to}
        aria-expanded={inside}
        className={`flex items-center justify-between gap-2 ${leaf} ${indent} ${inside ? opened : idle}`}
      >
        {t(one.label)}
        <Caret open={inside} />
      </Link>
      {inside && kids.map((kid) => <Leaf key={kid.to} one={kid} depth={depth + 1} shut={shut} />)}
    </>
  )
}

function UserMenu() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)

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
        className="flex items-center gap-2 rounded-lg px-1.5 py-1 text-[13px] text-ink-soft hover:bg-active"
      >
        <span className="flex size-7 items-center justify-center rounded-full bg-chip text-xs font-semibold text-chip-ink">
          {name.slice(0, 1).toUpperCase()}
        </span>
        <span className="hidden max-w-40 truncate sm:block">{name}</span>
        <Chevron open={open} />
      </button>

      {open && (
        <>
          <div className="fixed inset-0 z-30" onMouseDown={() => setOpen(false)} />
          <div className={`absolute right-0 z-40 mt-1 w-56 ${menu}`}>
            <div className="border-b border-line-menu px-2.5 pt-1 pb-2">
              <div className="truncate text-[13px] text-ink">{name}</div>
              <div className="truncate text-xs text-faint">{user === null ? "" : user.role}</div>
            </div>

            <div className="border-b border-line-menu px-2.5 py-2">
              <LanguagePicker className="w-full" />
            </div>

            <Link to="/account/password" onClick={() => setOpen(false)} className={`block w-full ${menuItem}`}>
              {t("password.title")}
            </Link>

            <button type="button" onClick={() => void leave()} className={`flex w-full items-center gap-2 ${menuItem}`}>
              <Door />
              {t("layout.signOut")}
            </button>
          </div>
        </>
      )}
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
      className={`size-4 text-faint ${open ? "-rotate-90" : "rotate-90"}`}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
    >
      <path d="m9 6 6 6-6 6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

function Caret({ open }: { open: boolean }) {
  return (
    <svg
      viewBox="0 0 24 24"
      className={`size-3.5 shrink-0 text-faint ${open ? "rotate-90" : ""}`}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      aria-hidden
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
