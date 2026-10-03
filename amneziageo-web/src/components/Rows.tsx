import { Fragment, useState } from "react"
import type { MouseEvent, PointerEvent, ReactNode } from "react"
import { SortControl } from "@/components/SortControl"
import { ariaSort, useOrder, useSorted } from "@/components/sort"
import type { Order, SortValue } from "@/components/sort"
import { quiet } from "@/components/styles"
import { useText } from "@/i18n"
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

export interface Choice {
  chosen: ReadonlySet<string | number>
  onChange: (chosen: Set<string | number>) => void
  title: string
  every: string
  actions?: ReactNode
}

// The least width of a column that shares what the fixed ones leave.
const least = 104

// The narrowest a column is dragged to.
const narrowest = 48

// The width of the column with the grips.
const grip = 36

// The width of the column with the boxes that choose rows.
const box = 40

const widthsKey = "amneziageo.columns"

export function Rows<T>({
  items,
  columns,
  keyOf,
  name = "",
  tools,
  drag,
  choice,
  empty,
}: {
  items: T[]
  columns: Column<T>[]
  keyOf: (item: T) => string | number
  name?: string
  tools?: ReactNode
  drag?: Drag<T>
  choice?: Choice
  empty?: string
}) {
  const t = useText()
  const wide = useAbove(wideQuery)
  const [held, setHeld] = useState<T | null>(null)
  const [over, setOver] = useState<T | null>(null)
  const table = name.length > 0 ? name : columns.map((column) => column.key).join(",")
  const [widths, setWidths] = useState<Record<string, number>>(() => storedWidths(table))
  const [pulled, setPulled] = useState<string | null>(null)
  const { order, toggle, choose, direct } = useOrder(name)
  const chosen = columns.find((column) => column.key === order?.key)
  const sorted = useSorted(items, chosen?.sort, order)
  const sortable = columns.filter((column) => column.sort && !column.tail)
  const picking = !wide && sortable.length > 0
  const moving = drag !== undefined && order === null
  const able = choice === undefined ? [] : sorted.map((place) => keyOf(place.item))
  const marked = choice === undefined ? 0 : able.filter((key) => choice.chosen.has(key)).length

  function widthOf(column: Column<T>): number | undefined {
    return column.tail ? column.width : (widths[column.key] ?? column.width)
  }

  // The column that takes what the others leave once every one of them has a width of its own.
  const spare = columns.every((column) => widthOf(column) !== undefined)
    ? (columns.find((column) => column.tail) ?? columns.at(-1))
    : undefined
  const span =
    columns.reduce((sum, column) => sum + (widthOf(column) ?? least), 0) + (moving ? grip : 0) + (choice ? box : 0)

  function lit(item: T): string {
    if (held === item) {
      return "opacity-60"
    }

    if (over === item) {
      return "bg-active"
    }

    return choice !== undefined && choice.chosen.has(keyOf(item)) ? "bg-active/60" : ""
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

  function clear() {
    if (choice === undefined) {
      return
    }

    const next = new Set(choice.chosen)
    for (const key of able) {
      next.delete(key)
    }

    choice.onChange(next)
  }

  function pull(column: Column<T>, event: PointerEvent<HTMLSpanElement>) {
    const cell = event.currentTarget.parentElement
    if (cell === null || event.button !== 0) {
      return
    }

    event.preventDefault()
    event.stopPropagation()
    const handle = event.currentTarget
    const from = event.clientX
    const start = cell.getBoundingClientRect().width
    let last = Math.round(start)
    handle.setPointerCapture(event.pointerId)
    setPulled(column.key)

    const move = (moved: globalThis.PointerEvent) => {
      last = Math.max(narrowest, Math.round(start + moved.clientX - from))
      setWidths((now) => ({ ...now, [column.key]: last }))
    }

    const done = () => {
      handle.removeEventListener("pointermove", move)
      handle.removeEventListener("pointerup", done)
      handle.removeEventListener("pointercancel", done)
      const next = { ...widths, [column.key]: last }
      setPulled(null)
      setWidths(next)
      keepWidths(table, next)
    }

    handle.addEventListener("pointermove", move)
    handle.addEventListener("pointerup", done)
    handle.addEventListener("pointercancel", done)
  }

  function reset(column: Column<T>) {
    const next = { ...widths }
    delete next[column.key]
    setWidths(next)
    keepWidths(table, next)
  }

  function tick(item: T) {
    if (choice === undefined) {
      return null
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

  const bar = (picking || tools !== undefined) && (
    <div className={`flex gap-3 border-b border-line px-4 py-3 ${wide ? "flex-wrap items-center" : "flex-col"}`}>
      <div className={wide ? "max-w-full min-w-0 flex-1" : ""}>{tools}</div>
      {picking && <SortControl options={sortable} order={order} choose={choose} direct={direct} fill />}
    </div>
  )

  const batch = choice !== undefined && marked > 0 && (
    <div className="flex flex-wrap items-center gap-2 border-b border-line bg-active px-4 py-2">
      <span className="text-[13px] font-medium whitespace-nowrap text-ink">
        {t("table.chosen", { count: marked })}
      </span>
      {choice.actions !== undefined && <span className="mx-1 hidden h-5 w-px bg-line-button sm:block" aria-hidden />}
      {choice.actions}
      <button type="button" onClick={clear} className={`ml-auto ${quiet}`}>
        {t("table.clear")}
      </button>
    </div>
  )

  const nothing = items.length === 0 && (
    <div className="border-t border-line-soft px-4 py-6 text-center text-sm text-muted">
      {empty ?? t("table.nothing")}
    </div>
  )

  if (wide) {
    return (
      <div>
        {bar}
        {batch}
        <div className="overflow-x-auto">
          <table
            className={`w-full table-fixed text-left text-sm tabular-nums ${pulled === null ? "" : "cursor-col-resize select-none"}`}
            style={{ minWidth: span }}
          >
            <colgroup>
              {choice && <col style={{ width: box }} />}
              {moving && <col style={{ width: grip }} />}
              {columns.map((column) => {
                const width = column === spare ? undefined : widthOf(column)

                return <col key={column.key} style={width === undefined ? undefined : { width }} />
              })}
            </colgroup>
            <thead className="bg-canvas text-xs text-muted">
              <tr>
                {choice && <th className="py-2.5 pr-0 pl-4">{all}</th>}
                {moving && <th className="py-2.5 pr-0 pl-3" />}
                {columns.map((column) => (
                  <th
                    key={column.key}
                    aria-sort={ariaSort(column.key, order)}
                    className={`relative overflow-hidden px-3 py-2.5 font-medium whitespace-nowrap ${column.head ?? ""}`}
                  >
                    {column.tail ? (
                      ""
                    ) : column.sort ? (
                      <SortCaption caption={column.caption} name={column.key} order={order} toggle={toggle} />
                    ) : (
                      <span className="block truncate">{column.caption}</span>
                    )}
                    {!column.tail && (
                      <span
                        role="separator"
                        aria-orientation="vertical"
                        title={t("table.resize")}
                        onPointerDown={(event) => pull(column, event)}
                        onDoubleClick={() => reset(column)}
                        className="group absolute inset-y-0 right-0 flex w-2.5 cursor-col-resize touch-none justify-end"
                      >
                        <span
                          className={`my-2 w-px ${pulled === column.key ? "bg-brand" : "bg-line group-hover:bg-brand"}`}
                        />
                      </span>
                    )}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {sorted.map((place) => (
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
                  {choice && <td className="py-3.25 pr-0 pl-4">{tick(place.item)}</td>}
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
          {nothing}
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
      {batch}

      {all && (
        <label className="flex items-center gap-3 px-4 py-2.5 text-xs text-faint">
          {all}
          {choice?.every}
        </label>
      )}

      {sorted.map((place) => (
        <div key={keyOf(place.item)} className={`border-t border-line-soft px-4 py-3.5 hover:bg-hover ${lit(place.item)}`}>
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

      {nothing}
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
      className={`group inline-flex max-w-full items-center gap-1 hover:text-ink ${active ? "text-ink" : ""}`}
    >
      <span className="truncate">{caption}</span>
      {active ? (
        <span className="text-[9px] text-brand-ink" aria-hidden>
          {order.down ? "▼" : "▲"}
        </span>
      ) : (
        <span className="text-[10px] text-faint opacity-0 group-hover:opacity-100" aria-hidden>
          ↕
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

// Reads the widths the columns of a list were dragged to.
function storedWidths(table: string): Record<string, number> {
  try {
    const kept = (JSON.parse(localStorage.getItem(widthsKey) ?? "{}") as Record<string, unknown>)[table]
    const widths: Record<string, number> = {}
    if (kept !== null && typeof kept === "object") {
      for (const [key, width] of Object.entries(kept)) {
        if (typeof width === "number" && Number.isFinite(width) && width >= narrowest) {
          widths[key] = width
        }
      }
    }

    return widths
  } catch {
    return {}
  }
}

// Remembers the widths the columns of a list were dragged to.
function keepWidths(table: string, widths: Record<string, number>) {
  try {
    const kept = JSON.parse(localStorage.getItem(widthsKey) ?? "{}") as Record<string, unknown>
    if (Object.keys(widths).length === 0) {
      delete kept[table]
    } else {
      kept[table] = widths
    }

    localStorage.setItem(widthsKey, JSON.stringify(kept))
  } catch {
    return
  }
}
