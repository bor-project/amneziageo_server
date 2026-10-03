import { NavLink } from "react-router-dom"

export interface Tab {
  to: string
  label: string
  end?: boolean
}

const tab = "-mb-px shrink-0 border-b-2 px-3.5 py-2.5 text-sm whitespace-nowrap"
const on = "border-brand font-medium text-ink"
const off = "border-transparent text-muted hover:border-line-button hover:text-ink"

export function Tabs({ items }: { items: Tab[] }) {
  return (
    <nav className="mt-2 flex gap-1 overflow-x-auto border-b border-line">
      {items.map((one) => (
        <NavLink
          key={one.to}
          to={one.to}
          end={one.end}
          className={({ isActive }) => `${tab} ${isActive ? on : off}`}
        >
          {one.label}
        </NavLink>
      ))}
    </nav>
  )
}
