import { useContext, useMemo, useState } from "react"
import type { ReactNode } from "react"
import { Link } from "react-router-dom"
import { Held } from "@/components/crumbs"
import type { Crumb } from "@/components/crumbs"
import { useAbove, wideQuery } from "@/theme/width"

export function CrumbsHolder({ children }: { children: ReactNode }) {
  const [head, setHead] = useState<Crumb[]>([])
  const [tail, setTail] = useState<Crumb[]>([])
  const value = useMemo(() => ({ head, tail, putHead: setHead, putTail: setTail }), [head, tail])

  return <Held.Provider value={value}>{children}</Held.Provider>
}

export function Crumbs() {
  const { head, tail } = useContext(Held)
  const wide = useAbove(wideQuery)
  const items = [...head, ...tail]
  const shown = wide || items.length < 3 ? items : items.slice(-2)
  const root = items[0]

  return (
    <nav className="flex min-w-0 items-center gap-1.5 text-[13px]">
      {shown.length < items.length && root?.to !== undefined && (
        <>
          <Link to={root.to} className="rounded-md p-1 text-muted hover:bg-active hover:text-ink">
            &hellip;
          </Link>
          <span className="text-faint" aria-hidden>
            /
          </span>
        </>
      )}

      {shown.map((one, at) => (
        <div key={one.label} className="flex min-w-0 items-center gap-1.5">
          {at > 0 && (
            <span className="text-faint" aria-hidden>
              /
            </span>
          )}
          <Piece crumb={one} />
        </div>
      ))}
    </nav>
  )
}

function Piece({ crumb }: { crumb: Crumb }) {
  if (crumb.to === undefined) {
    return <span className="truncate font-medium text-ink">{crumb.label}</span>
  }

  return (
    <Link to={crumb.to} className="truncate rounded-md p-1 text-muted hover:bg-active hover:text-ink">
      {crumb.label}
    </Link>
  )
}
