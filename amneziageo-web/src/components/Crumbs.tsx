import { Link } from "react-router-dom"

export interface Crumb {
  to?: string
  label: string
}

export function Crumbs({ items }: { items: Crumb[] }) {
  return (
    <nav className="mt-4 flex flex-wrap items-center gap-2 text-sm text-muted">
      {items.map((one, at) => (
        <div key={one.label} className="flex items-center gap-2">
          {at > 0 && <span aria-hidden>/</span>}
          {one.to === undefined ? (
            <span className="text-ink">{one.label}</span>
          ) : (
            <Link to={one.to} className="hover:text-brand-ink">
              {one.label}
            </Link>
          )}
        </div>
      ))}
    </nav>
  )
}
