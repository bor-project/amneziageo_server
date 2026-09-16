import { useState } from "react"
import { menu, menuItem } from "@/components/styles"
import { useText } from "@/i18n"
import type { Order } from "@/components/sort"

const pick =
  "flex items-center justify-between gap-2 rounded-lg border border-line-input bg-input px-2.5 py-1.75 text-[13px] text-ink hover:border-line-button"

const way = "rounded-md px-2.5 py-1.25 text-[13px]"

export function SortControl({
  options,
  order,
  choose,
  direct,
  fill = false,
}: {
  options: { key: string; caption: string }[]
  order: Order | null
  choose: (key: string) => void
  direct: (key: string, down: boolean) => void
  fill?: boolean
}) {
  const t = useText()
  const [open, setOpen] = useState(false)
  const first = options[0]

  if (first === undefined) {
    return null
  }

  const held = options.find((one) => one.key === order?.key)

  return (
    <div className={`flex items-center gap-2 ${fill ? "w-full" : ""}`}>
      <span className="shrink-0 text-xs text-faint">{t("sort.label")}</span>

      <div className={`relative ${fill ? "min-w-0 flex-1" : ""}`}>
        <button type="button" onClick={() => setOpen(!open)} className={`${pick} ${fill ? "w-full" : ""}`}>
          <span className="truncate">{held?.caption ?? t("sort.none")}</span>
          <span className="text-[10px] text-faint" aria-hidden>
            &#9662;
          </span>
        </button>

        {open && (
          <>
            <div className="fixed inset-0 z-40" onMouseDown={() => setOpen(false)} />
            <div className={`absolute right-0 z-50 mt-1 max-h-80 min-w-48 overflow-y-auto ${menu}`}>
              {options.map((one) => (
                <button
                  key={one.key}
                  type="button"
                  onClick={() => {
                    setOpen(false)
                    choose(one.key)
                  }}
                  className={`flex w-full items-center justify-between gap-3 ${menuItem}`}
                >
                  <span className="truncate">{one.caption}</span>
                  {one.key === order?.key && (
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

      <div className="flex shrink-0 gap-0.5 rounded-lg border border-line-input bg-input p-0.5">
        <Way
          caption={t("sort.up")}
          glyph="↑"
          on={order !== null && !order.down}
          onPick={() => direct(first.key, false)}
        />
        <Way
          caption={t("sort.down")}
          glyph="↓"
          on={order?.down === true}
          onPick={() => direct(first.key, true)}
        />
      </div>
    </div>
  )
}

function Way({
  caption,
  glyph,
  on,
  onPick,
}: {
  caption: string
  glyph: string
  on: boolean
  onPick: () => void
}) {
  return (
    <button
      type="button"
      title={caption}
      aria-label={caption}
      aria-pressed={on}
      onClick={onPick}
      className={`${way} ${on ? "bg-picked text-white" : "text-muted hover:text-ink"}`}
    >
      <span aria-hidden>{glyph}</span>
    </button>
  )
}
