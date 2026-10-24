import type { ReactNode } from "react"
import { NavLink } from "react-router-dom"

export interface Tab {
  to: string
  label: string
  end?: boolean
  mark?: ReactNode
}

const tab = "shrink-0 border-b-2 px-3.5 py-2.5 text-sm whitespace-nowrap"
const on = "border-brand font-medium text-ink"
const off = "border-transparent text-muted hover:border-line-button hover:text-ink"

export function Tabs({ items }: { items: Tab[] }) {
  return (
    <div className="mt-2 border-b border-line">
      <nav className="-mb-px flex gap-1 overflow-x-auto">
        {items.map((one) => (
          <span key={one.to} className="flex shrink-0 items-center">
            <NavLink to={one.to} end={one.end} className={({ isActive }) => `${tab} ${isActive ? on : off}`}>
              {one.label}
            </NavLink>
            {one.mark !== undefined && <span className="-ml-2 pr-2">{one.mark}</span>}
          </span>
        ))}
      </nav>
    </div>
  )
}
