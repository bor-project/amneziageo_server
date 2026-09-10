import { useState } from "react"
import type { MouseEvent } from "react"
import { card, primary, quiet } from "@/components/styles"

export interface RowAction {
  label: string
  onPick: () => void
  alarming?: boolean
}

const width = 176

export function RowActions({ title, actions, trigger }: { title: string; actions: RowAction[]; trigger?: string }) {
  const [spot, setSpot] = useState<{ top: number; left: number } | null>(null)

  function toggle(event: MouseEvent<HTMLButtonElement>) {
    if (spot) {
      setSpot(null)

      return
    }

    const box = event.currentTarget.getBoundingClientRect()
    setSpot({ top: box.bottom + 4, left: Math.max(8, box.right - width) })
  }

  function pick(action: RowAction) {
    setSpot(null)
    action.onPick()
  }

  return (
    <div className="flex justify-end">
      {trigger === undefined ? (
        <button type="button" title={title} aria-label={title} onClick={toggle} className={quiet}>
          <Kebab />
        </button>
      ) : (
        <button type="button" onClick={toggle} className={primary}>
          {trigger}
        </button>
      )}

      {spot && (
        <>
          <div className="fixed inset-0 z-30" onMouseDown={() => setSpot(null)} />
          <div
            style={{ top: spot.top, left: spot.left, width }}
            className={`fixed z-40 flex flex-col py-1 shadow-lg ${card}`}
          >
            {actions.map((action) => (
              <button
                key={action.label}
                type="button"
                onClick={() => pick(action)}
                className={`px-3 py-2 text-left text-sm hover:bg-hover ${action.alarming ? "text-alarm" : "text-muted hover:text-brand-ink"}`}
              >
                {action.label}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}

function Kebab() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="currentColor">
      <circle cx="12" cy="5" r="1.6" />
      <circle cx="12" cy="12" r="1.6" />
      <circle cx="12" cy="19" r="1.6" />
    </svg>
  )
}
