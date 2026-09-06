import { useState } from "react"
import type { MouseEvent } from "react"
import { card, quiet } from "@/components/styles"

export interface RowAction {
  label: string
  onPick: () => void
  alarming?: boolean
}

const width = 176

export function RowActions({ title, actions }: { title: string; actions: RowAction[] }) {
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
      <button type="button" title={title} aria-label={title} onClick={toggle} className={quiet}>
        <Pencil />
      </button>

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

function Pencil() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d="M4 20h4l10-10a2.8 2.8 0 0 0-4-4L4 16v4z" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}
