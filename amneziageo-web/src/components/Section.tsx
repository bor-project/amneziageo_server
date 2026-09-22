import { useEffect } from "react"
import { Link, Outlet, useLocation } from "react-router-dom"
import { Glyph } from "@/components/Glyph"
import { useCrumbs } from "@/components/crumbs"
import { here, place } from "@/components/menu"
import type { Item } from "@/components/menu"
import { card, primary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"
import { keepSpot, sectionOf } from "@/store/spots"

const tile = "flex size-10 shrink-0 items-center justify-center rounded-lg bg-chip text-chip-ink group-hover:bg-picked"

export function Sectioned({ title, items }: { title: TextKey; items: Item[] }) {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const { pathname, search } = useLocation()
  const open = items.filter((one) => holds(user, one.scope))
  const now = here(open, pathname)
  const listed = now !== undefined && pathname === now.to
  const add =
    now !== undefined && listed && now.add !== undefined && holds(user, now.add.scope) ? now.add.to : null

  useEffect(() => {
    if (listed) {
      keepSpot(pathname, search)
    }
  }, [listed, pathname, search])

  useCrumbs(
    now === undefined
      ? [{ label: t(title) }]
      : [
          { label: t(title), to: `/${sectionOf(pathname)}` },
          { label: t(now.label), to: place(now.to) },
        ],
  )

  return (
    <div>
      <div className="flex items-start justify-between gap-4">
        <h1 className="text-2xl leading-10 font-semibold tracking-[-0.02em]">
          {t(now === undefined ? title : now.label)}
        </h1>
        {add !== null && (
          <Link to={add} className={`flex h-10 shrink-0 items-center ${primary}`}>
            {t("action.add")}
          </Link>
        )}
      </div>

      <Outlet />
    </div>
  )
}

export function Cards({ items }: { items: Item[] }) {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)

  return (
    <div className="mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {items
        .filter((one) => holds(user, one.scope))
        .map((one) => (
          <Link
            key={one.to}
            to={place(one.to)}
            className={`group flex items-start gap-3.5 p-4.5 hover:border-brand hover:bg-active ${card}`}
          >
            <span className={tile}>
              <Glyph name={one.icon} />
            </span>
            <span className="min-w-0">
              <span className="block text-[15px] font-medium text-ink">{t(one.label)}</span>
              <span className="mt-1 block text-[13px] leading-5 text-muted">{t(one.about)}</span>
            </span>
          </Link>
        ))}
    </div>
  )
}
