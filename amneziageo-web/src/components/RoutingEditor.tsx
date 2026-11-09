import { useLayoutEffect, useRef } from "react"
import type { ReactNode } from "react"
import { Navigate, useLocation, useParams } from "react-router-dom"
import { Glyph } from "@/components/Glyph"
import { Sectioned } from "@/components/Section"
import { routing, routingTabs, under } from "@/components/menu"
import { primary, quiet } from "@/components/styles"
import { useText } from "@/i18n"

export function RoutingSection() {
  const { pathname } = useLocation()
  const tabbed = pathname === "/routing" || routingTabs.some((item) => under(pathname, item.to))

  return <Sectioned title="nav.routing" to="/routing" items={tabbed ? routingTabs : routing} tabbed={tabbed} wrapTabs />
}

export function RoutingLegacy({ to }: { to: string }) {
  const params = useParams()
  const location = useLocation()
  const search = new URLSearchParams(location.search)
  search.set("edit", params.ruleId ?? params.outboundId ?? params.balancerId ?? "new")

  return <Navigate to={to + "?" + search.toString() + location.hash} replace />
}

export function RoutingToolbar({ onAdd, children }: { onAdd?: () => void; children?: ReactNode }) {
  const t = useText()

  return (
    <div className="mt-4 mb-4 flex flex-wrap items-end gap-3">
      {children}
      {onAdd && <button type="button" onClick={onAdd} className={"ml-auto " + primary}>{t("action.add")}</button>}
    </div>
  )
}

export function RoutingEditor({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const t = useText()
  const ref = useRef<HTMLElement>(null)

  useLayoutEffect(() => {
    ref.current?.closest("[data-routing-scroll]")?.scrollTo({ left: 0 })
  }, [])

  return (
    <section ref={ref} aria-label={title} className="routing-editor border-y border-line bg-canvas px-4 py-4">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-base font-semibold text-ink">{title}</h2>
        <button type="button" className={quiet} onClick={onClose} aria-label={t("action.close")}>✕</button>
      </div>
      {children}
    </section>
  )
}

export function RoutingSettings({ onClick, open = false }: { onClick: () => void; open?: boolean }) {
  const t = useText()

  return (
    <button type="button" className={quiet} onClick={onClick} aria-label={t("action.settings")}
      title={t("action.settings")} aria-expanded={open}>
      <Glyph name="gear" />
    </button>
  )
}
