import { createContext, useContext, useEffect, useState } from "react"
import type { ReactNode } from "react"
import { createPortal } from "react-dom"
import { Link, Outlet, useLocation } from "react-router-dom"
import { Glyph } from "@/components/Glyph"
import { Tabs } from "@/components/Tabs"
import { Held, useCrumbs } from "@/components/crumbs"
import type { Crumb } from "@/components/crumbs"
import { here } from "@/components/menu"
import type { Item } from "@/components/menu"
import { card, primary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppDispatch, useAppSelector } from "@/store/hooks"
import { spotIn, useSpots } from "@/store/spots"
import { spotKept } from "@/store/spotsSlice"

const tile = "flex size-10 shrink-0 items-center justify-center rounded-lg bg-chip text-chip-ink group-hover:bg-picked"

// Where a page puts the buttons that stand beside its title.
const Slot = createContext<HTMLElement | null>(null)

// A part of the panel: its title, the button that adds to the list it shows, the tabs of its lists when it has
// several, and the page under them. A page under a list is titled by the first crumb it adds.
export function Sectioned({
  title,
  to,
  items,
  tabbed = false,
}: {
  title: TextKey
  to: string
  items: Item[]
  tabbed?: boolean
}) {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const dispatch = useAppDispatch()
  const spots = useSpots()
  const { tail } = useContext(Held)
  const { pathname, search } = useLocation()
  const [slot, setSlot] = useState<HTMLDivElement | null>(null)
  const open = items.filter((one) => holds(user, one.scope))
  const now = here(open, pathname)
  const listed = now !== undefined && pathname === now.to
  const add =
    now !== undefined && listed && now.add !== undefined && holds(user, now.add.scope) ? now.add.to : null
  const crumbs: Crumb[] = [{ label: t(title), to }]
  if (now !== undefined && now.to !== to) {
    crumbs.push({ label: t(now.label), to: spotIn(spots, now.to) })
  }

  useEffect(() => {
    if (listed) {
      dispatch(spotKept({ path: pathname, search }))
    }
  }, [dispatch, listed, pathname, search])

  useCrumbs(crumbs)

  const named = listed || now === undefined ? null : (tail[0]?.label ?? null)
  const heading = named ?? (now === undefined || tabbed ? t(title) : t(now.label))

  return (
    <div>
      <div className="flex items-start justify-between gap-4">
        <h1 className="min-w-0 truncate text-2xl leading-10 font-semibold tracking-[-0.02em]" title={heading}>
          {heading}
        </h1>
        <div ref={setSlot} className="flex shrink-0 flex-wrap items-center justify-end gap-2">
          {add !== null && (
            <Link to={add} className={`flex h-10 shrink-0 items-center ${primary}`}>
              {t("action.add")}
            </Link>
          )}
        </div>
      </div>

      {tabbed && listed && open.length > 1 && (
        <Tabs
          items={open.map((one) => ({ to: spotIn(spots, one.to), label: t(one.label), end: one.to === to }))}
        />
      )}

      <Slot.Provider value={slot}>
        <Outlet />
      </Slot.Provider>
    </div>
  )
}

// Puts the buttons of a page beside its title.
export function HeadActions({ children }: { children: ReactNode }) {
  const slot = useContext(Slot)

  return slot === null ? null : createPortal(children, slot)
}

export function Cards({ items }: { items: Item[] }) {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const spots = useSpots()

  return (
    <div className="mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {items
        .filter((one) => holds(user, one.scope))
        .map((one) => (
          <Link
            key={one.to}
            to={spotIn(spots, one.to)}
            className={`group flex items-start gap-3.5 p-4.5 hover:border-brand hover:bg-active ${card}`}
          >
            <span className={tile}>
              <Glyph name={one.icon} />
            </span>
            <span className="min-w-0">
              <span className="block text-[15px] font-medium text-ink">{t(one.label)}</span>
              {one.about !== undefined && (
                <span className="mt-1 block text-[13px] leading-5 text-muted">{t(one.about)}</span>
              )}
            </span>
          </Link>
        ))}
    </div>
  )
}
