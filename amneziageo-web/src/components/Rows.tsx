import { Fragment, useState } from "react"
import type { MouseEvent, ReactNode } from "react"
import { SortControl } from "@/components/SortControl"
import { ariaSort, useOrder, useSorted } from "@/components/sort"
import type { Order, Place, SortValue } from "@/components/sort"
import { useAbove, wideQuery } from "@/theme/width"

export interface Column<T> {
  key: string
  caption: string
  cell: (item: T, at: number) => ReactNode
  sort?: (item: T) => SortValue
  lead?: boolean
  tail?: boolean
  width?: number
  wrap?: boolean
  head?: string
  body?: string
}

export interface Drag<T> {
  title: string
  onMove: (item: T, to: T) => void
}

export interface Choice<T> {
  chosen: ReadonlySet<string | number>
  onChange: (chosen: Set<string | number>) => void
  able: (item: T) => boolean
  title: string
  every: string
}

// The least width of a column that shares what the fixed ones leave.
const least = 104

// The width of the column with the grips.
const grip = 36

// The width of the column with the boxes that choose rows.
const box = 36

export function Rows<T>({
  items,
  columns,
  keyOf,
  arrange,
  name = "",
  tools,
  drag,
  choice,
}: {
  items: T[]
  columns: Column<T>[]
  keyOf: (item: T) => string | number
  arrange?: (items: T[]) => T[]
  name?: string
  tools?: ReactNode
  drag?: Drag<T>
  choice?: Choice<T>
}) {
  const wide = useAbove(wideQuery)
  const [held, setHeld] = useState<T | null>(null)
  const [over, setOver] = useState<T | null>(null)
  const { order, toggle, choose, direct } = useOrder(name)
  const chosen = columns.find((column) => column.key === order?.key)
  const sorted = useSorted(items, chosen?.sort, order)
  const places = arrange ? rearrange(sorted, arrange) : sorted
  const sortable = columns.filter((column) => column.sort && !column.tail)
  const moving = drag !== undefined && order === null
  const able = choice === undefined ? [] : places.map((place) => place.item).filter(choice.able).map(keyOf)
  const marked = choice === undefined ? 0 : able.filter((key) => choice.chosen.has(key)).length
  const span =
    columns.reduce((sum, column) => sum + (column.width ?? least), 0) + (moving ? grip : 0) + (choice ? box : 0)

  function lit(item: T): string {
    if (held === item) {
      return "opacity-60"
    }

    return over === item ? "bg-active" : ""
  }

  function drop(to: T) {
    if (drag !== undefined && held !== null && held !== to) {
      drag.onMove(held, to)
    }

    setHeld(null)
    setOver(null)
  }

  function flip(item: T) {
    if (choice === undefined) {
      return
    }

    const next = new Set(choice.chosen)
    const key = keyOf(item)
    if (next.has(key)) {
      next.delete(key)
    } else {
      next.add(key)
    }

    choice.onChange(next)
  }

  function flipAll() {
    if (choice === undefined) {
      return
    }

    const next = new Set(choice.chosen)
    const every = marked === able.length
    for (const key of able) {
      if (every) {
        next.delete(key)
      } else {
        next.add(key)
      }
    }

    choice.onChange(next)
  }

  function tick(item: T) {
    if (choice === undefined) {
      return null
    }

    if (!choice.able(item)) {
      return <span className="block size-4 shrink-0" />
    }

    return <Tick on={choice.chosen.has(keyOf(item))} title={choice.title} onChange={() => flip(item)} />
  }

  const all = choice !== undefined && able.length > 0 && (
    <Tick
      on={marked > 0 && marked === able.length}
      some={marked > 0 && marked < able.length}
      title={choice.every}
      onChange={flipAll}
    />
  )

  const bar = (sortable.length > 0 || tools !== undefined) && (
    <div
      className={`flex gap-3 border-b border-line px-4 py-3 ${wide ? "flex-wrap items-center" : "flex-col"}`}
    >
      <div className={wide ? "max-w-full shrink-0" : ""}>{tools}</div>
      {sortable.length > 0 && (
        <div className={wide ? "ml-auto shrink-0" : ""}>
          <SortControl options={sortable} order={order} choose={choose} direct={direct} fill={!wide} />
        </div>
      )}
    </div>
  )

  if (wide) {
    return (
      <div>
        {bar}
        <div className="overflow-x-auto">
          <table className="w-full table-fixed text-left text-sm tabular-nums" style={{ minWidth: span }}>
            <colgroup>
              {choice && <col style={{ width: box }} />}
              {moving && <col style={{ width: grip }} />}
              {columns.map((column) => (
                <col key={column.key} style={column.width === undefined ? undefined : { width: column.width }} />
              ))}
            </colgroup>
            <thead className="text-xs text-faint">
              <tr>
                {choice && <th className="py-2.5 pr-0 pl-3">{all}</th>}
                {moving && <th className="py-2.5 pr-0 pl-3" />}
                {columns.map((column) => (
                  <th
                    key={column.key}
                    aria-sort={ariaSort(column.key, order)}
                    className={`overflow-hidden px-3 py-2.5 font-normal whitespace-nowrap ${column.head ?? ""}`}
                  >
                    {column.tail ? (
                      ""
                    ) : column.sort ? (
                      <SortCaption caption={column.caption} name={column.key} order={order} toggle={toggle} />
                    ) : (
                      column.caption
                    )}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {places.map((place) => (
                <tr
                  key={keyOf(place.item)}
                  draggable={moving && held === place.item}
                  onDragStart={(e) => {
                    e.dataTransfer.effectAllowed = "move"
                    e.dataTransfer.setData("text/plain", String(keyOf(place.item)))
                  }}
                  onDragOver={(e) => {
                    if (moving && held !== null) {
                      e.preventDefault()
                      setOver(place.item)
                    }
                  }}
                  onDrop={(e) => {
                    e.preventDefault()
                    drop(place.item)
                  }}
                  onDragEnd={() => {
                    setHeld(null)
                    setOver(null)
                  }}
                  className={`border-t border-line-soft hover:bg-hover ${lit(place.item)}`}
                >
                  {choice && <td className="py-3.25 pr-0 pl-3">{tick(place.item)}</td>}
                  {moving && (
                    <td className="py-3.25 pr-0 pl-3">
                      <button
                        type="button"
                        title={drag.title}
                        aria-label={drag.title}
                        onPointerDown={() => setHeld(place.item)}
                        onPointerUp={() => setHeld(null)}
                        className="cursor-grab text-faint hover:text-ink active:cursor-grabbing"
                      >
                        <Grip />
                      </button>
                    </td>
                  )}
                  {columns.map((column) => (
                    <td
                      key={column.key}
                      onMouseEnter={column.tail || column.wrap ? undefined : hint}
                      className={`px-3 py-3.25 text-body ${column.tail ? "" : column.wrap ? "break-words" : "truncate"} ${column.body ?? ""}`}
                    >
                      {column.cell(place.item, place.at)}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    )
  }

  const lead = columns.find((column) => column.lead) ?? columns[0]
  const tail = columns.find((column) => column.tail)
  const rest = columns.filter((column) => column !== lead && column !== tail)

  return (
    <div>
      {bar}

      {all && (
        <label className="flex items-center gap-3 px-4 py-2.5 text-xs text-faint">
          {all}
          {choice?.every}
        </label>
      )}

      {places.map((place) => (
        <div key={keyOf(place.item)} className="border-t border-line-soft px-4 py-3.5 hover:bg-hover">
          <div className="flex items-start justify-between gap-3">
            <div className="flex min-w-0 items-start gap-3">
              {choice && <span className="mt-1 shrink-0">{tick(place.item)}</span>}
              <div className="min-w-0 text-[15px] font-semibold text-ink">{lead.cell(place.item, place.at)}</div>
            </div>
            {tail && <div className="shrink-0">{tail.cell(place.item, place.at)}</div>}
          </div>

          <dl className="mt-2 grid grid-cols-[minmax(0,10rem)_minmax(0,1fr)] gap-x-3 gap-y-1">
            {rest.map((column) => told(column.cell(place.item, place.at), column))}
          </dl>
        </div>
      ))}
    </div>
  )
}

export function SortCaption({
  caption,
  name,
  order,
  toggle,
}: {
  caption: string
  name: string
  order: Order | null
  toggle: (key: string) => void
}) {
  const active = order?.key === name

  return (
    <button
      type="button"
      onClick={() => toggle(name)}
      className={`inline-flex items-center gap-1 hover:text-ink-soft ${active ? "text-ink-soft" : ""}`}
    >
      {caption}
      {active && (
        <span className="text-[9px] text-brand-ink" aria-hidden>
          {order.down ? "▼" : "▲"}
        </span>
      )}
    </button>
  )
}

function Tick({
  on,
  some = false,
  title,
  onChange,
}: {
  on: boolean
  some?: boolean
  title: string
  onChange: () => void
}) {
  return (
    <input
      type="checkbox"
      checked={on}
      title={title}
      aria-label={title}
      ref={(box) => {
        if (box !== null) {
          box.indeterminate = some
        }
      }}
      onChange={onChange}
      className="block size-4 cursor-pointer accent-brand"
    />
  )
}

function Grip() {
  return (
    <svg viewBox="0 0 16 16" className="size-4" fill="currentColor" aria-hidden>
      <circle cx="5.5" cy="3.5" r="1.3" />
      <circle cx="10.5" cy="3.5" r="1.3" />
      <circle cx="5.5" cy="8" r="1.3" />
      <circle cx="10.5" cy="8" r="1.3" />
      <circle cx="5.5" cy="12.5" r="1.3" />
      <circle cx="10.5" cy="12.5" r="1.3" />
    </svg>
  )
}

// Names the whole text of a cell that is cut short.
function hint(event: MouseEvent<HTMLTableCellElement>) {
  const cell = event.currentTarget
  if (cell.scrollWidth > cell.clientWidth) {
    cell.title = cell.innerText
  } else {
    cell.removeAttribute("title")
  }
}

function rearrange<T>(places: Place<T>[], arrange: (items: T[]) => T[]): Place<T>[] {
  const at = new Map(places.map((place) => [place.item, place.at]))

  return arrange(places.map((place) => place.item)).map((item) => ({ item, at: at.get(item) ?? 0 }))
}

function told<T>(value: ReactNode, column: Column<T>) {
  if (value === null || value === undefined || value === false || value === "") {
    return null
  }

  return (
    <Fragment key={column.key}>
      <dt className="text-xs text-faint">{column.caption}</dt>
      <dd className="min-w-0 text-[13px] text-body">{value}</dd>
    </Fragment>
  )
}
