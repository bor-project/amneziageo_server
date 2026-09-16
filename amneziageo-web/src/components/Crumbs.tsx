import { useContext, useMemo, useState } from "react"
import type { ReactNode } from "react"
import { Link, useNavigate } from "react-router-dom"
import { Held } from "@/components/crumbs"
import type { Crumb } from "@/components/crumbs"
import { menu, menuItem } from "@/components/styles"
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
  const [open, setOpen] = useState<number | null>(null)
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
          <Piece crumb={one} open={open === at} onOpen={(want) => setOpen(want ? at : null)} />
        </div>
      ))}
    </nav>
  )
}

function Piece({ crumb, open, onOpen }: { crumb: Crumb; open: boolean; onOpen: (want: boolean) => void }) {
  const navigate = useNavigate()

  if (crumb.options !== undefined) {
    return (
      <div className="relative">
        <button
          type="button"
          onClick={() => onOpen(!open)}
          className="flex items-center gap-1.5 rounded-md px-2 py-1 font-medium text-ink hover:bg-active"
        >
          <span className="max-w-48 truncate">{crumb.label}</span>
          <span className="text-[10px] text-faint" aria-hidden>
            &#9662;
          </span>
        </button>

        {open && (
          <>
            <div className="fixed inset-0 z-50" onMouseDown={() => onOpen(false)} />
            <div className={`absolute top-8.5 left-2 z-60 max-h-80 min-w-48 overflow-y-auto ${menu}`}>
              {crumb.options.map((one) => (
                <button
                  key={one.to}
                  type="button"
                  onClick={() => {
                    onOpen(false)
                    navigate(one.to)
                  }}
                  className={`flex w-full items-center justify-between gap-3 ${menuItem}`}
                >
                  <span className="truncate">{one.label}</span>
                  {one.mark === true && (
                    <span className="shrink-0 text-brand-ink" aria-hidden>
                      &#10003;
                    </span>
                  )}
                </button>
              ))}
            </div>
          </>
        )}
      </div>
    )
  }

  if (crumb.to === undefined) {
    return <span className="truncate font-medium text-ink">{crumb.label}</span>
  }

  return (
    <Link to={crumb.to} className="truncate rounded-md p-1 text-muted hover:bg-active hover:text-ink">
      {crumb.label}
    </Link>
  )
}
