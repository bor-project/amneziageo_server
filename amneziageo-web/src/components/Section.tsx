import { useEffect } from "react"
import { Link, Navigate, Outlet, useLocation } from "react-router-dom"
import { useCrumbs } from "@/components/crumbs"
import type { Crumb } from "@/components/crumbs"
import { here, under } from "@/components/menu"
import type { Item } from "@/components/menu"
import { primary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"
import { keepSpot, lastSpot, sectionOf } from "@/store/spots"

export function Sectioned({ title, items }: { title: TextKey; items: Item[] }) {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const { pathname, search } = useLocation()
  const open = items.filter((one) => holds(user, one.scope))
  const now = here(open, pathname)
  const kids = (now?.kids ?? []).filter((one) => holds(user, one.scope))
  const kid = here(kids, pathname)
  const spot = kid ?? now
  const listed = spot !== undefined && pathname === spot.to
  const add =
    spot !== undefined && pathname === spot.to && spot.add !== undefined && holds(user, spot.add.scope)
      ? spot.add.to
      : null

  useEffect(() => {
    if (listed) {
      keepSpot(pathname, search)
    }
  }, [listed, pathname, search])

  useCrumbs(trail(t, title, open, now, kid, pathname, listed))

  return (
    <div>
      {now !== undefined && (
        <div className="flex items-start justify-between gap-4">
          <h1 className="text-2xl leading-10 font-semibold tracking-[-0.02em]">{t(now.label)}</h1>
          {add !== null && (
            <Link to={add} className={`flex h-10 shrink-0 items-center ${primary}`}>
              {t("action.add")}
            </Link>
          )}
        </div>
      )}

      <Outlet />
    </div>
  )
}

function trail(
  t: Text,
  title: TextKey,
  items: Item[],
  open: Item | undefined,
  kid: Item | undefined,
  pathname: string,
  listed: boolean,
): Crumb[] {
  const root = items[0]?.to

  if (root === undefined) {
    return [{ label: t(title) }]
  }

  const spot = lastSpot(sectionOf(pathname), root)

  if (open === undefined) {
    return listed ? [{ label: t(title) }] : [{ label: t(title), to: spot }]
  }

  const trace: Crumb[] = [
    { label: t(title), to: spot },
    { label: t(open.label), to: under(spot, open.to) ? spot : open.to },
  ]

  if (kid !== undefined) {
    trace.push({ label: t(kid.label), to: under(spot, kid.to) ? spot : kid.to })
  }

  return listed ? trace.map((one) => ({ label: one.label })) : trace
}

export function Landing({ section, to }: { section: string; to: string }) {
  const { pathname } = useLocation()
  const spot = lastSpot(section, to)
  const self = spot === pathname || spot.startsWith(`${pathname}?`)

  return <Navigate to={self ? to : spot} replace />
}
