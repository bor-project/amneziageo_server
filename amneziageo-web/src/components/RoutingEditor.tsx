import type { ReactNode } from "react"
import { Glyph } from "@/components/Glyph"
import { Sectioned } from "@/components/Section"
import { routing } from "@/components/menu"
import { primary, quiet } from "@/components/styles"
import { useText } from "@/i18n"

export function RoutingSection() {
  return <Sectioned title="nav.routing" to="/routing" items={routing} />
}

export function RoutingToolbar({ onAdd, actions, children }: { onAdd?: () => void; actions?: ReactNode; children?: ReactNode }) {
  const t = useText()

  return (
    <div className="mt-4 mb-4 flex flex-wrap items-end gap-3">
      {children}
      {(onAdd || actions) && <div className="ml-auto flex items-center gap-2">
        {onAdd && <button type="button" onClick={onAdd} className={primary}>{t("action.add")}</button>}
        {actions}
      </div>}
    </div>
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
