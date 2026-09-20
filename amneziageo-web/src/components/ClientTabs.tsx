import { NavLink } from "react-router-dom"
import { useText } from "@/i18n"

const tab = "-mb-px shrink-0 border-b-2 px-3.5 py-2.5 text-sm"
const chosen = "border-brand font-medium text-ink"
const plain = "border-transparent text-muted hover:text-ink"

export function ClientTabs({ id, may }: { id: number; may: boolean }) {
  const t = useText()

  return (
    <div className="flex gap-4 overflow-x-auto overflow-y-hidden border-b border-line [scrollbar-width:none] sm:gap-6">
      <NavLink
        to={`/connections/clients/${id}/export`}
        className={({ isActive }) => `${tab} ${isActive ? chosen : plain}`}
      >
        {t("action.export")}
      </NavLink>
      {may && (
        <NavLink
          to={`/connections/clients/${id}/edit`}
          className={({ isActive }) => `${tab} ${isActive ? chosen : plain}`}
        >
          {t("action.settings")}
        </NavLink>
      )}
    </div>
  )
}
