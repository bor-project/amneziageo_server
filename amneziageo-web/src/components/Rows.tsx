import { Fragment } from "react"
import type { ReactNode } from "react"
import { ariaSort, useOrder, useSorted } from "@/components/sort"
import type { Order, Place, SortValue } from "@/components/sort"
import { roomyQuery, useAbove } from "@/theme/width"

export interface Column<T> {
  key: string
  caption: string
  cell: (item: T, at: number) => ReactNode
  sort?: (item: T) => SortValue
  lead?: boolean
  tail?: boolean
  head?: string
  body?: string
}

export function Rows<T>({
  items,
  columns,
  keyOf,
  arrange,
}: {
  items: T[]
  columns: Column<T>[]
  keyOf: (item: T) => string | number
  arrange?: (items: T[]) => T[]
}) {
  const roomy = useAbove(roomyQuery)
  const { order, toggle } = useOrder()
  const chosen = columns.find((column) => column.key === order?.key)
  const sorted = useSorted(items, chosen?.sort, order)
  const places = arrange ? rearrange(sorted, arrange) : sorted

  if (roomy) {
    return (
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="text-xs text-muted">
            <tr>
              {columns.map((column) => (
                <th
                  key={column.key}
                  aria-sort={ariaSort(column.key, order)}
                  className={`px-4 py-2 font-normal ${column.head ?? ""}`}
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
              <tr key={keyOf(place.item)} className="border-t border-line">
                {columns.map((column) => (
                  <td key={column.key} className={`px-4 py-2 ${column.body ?? ""}`}>
                    {column.cell(place.item, place.at)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    )
  }

  const lead = columns.find((column) => column.lead) ?? columns[0]
  const tail = columns.find((column) => column.tail)
  const rest = columns.filter((column) => column !== lead && column !== tail)

  return (
    <div>
      <SortBar options={columns.filter((column) => column.sort && !column.tail)} order={order} toggle={toggle} />

      {places.map((place) => (
        <div key={keyOf(place.item)} className="border-t border-line px-4 py-3">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0 text-sm font-medium text-ink">{lead.cell(place.item, place.at)}</div>
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
      className={`inline-flex items-center gap-1 hover:text-ink ${active ? "text-ink" : ""}`}
    >
      {caption}
      {active && <span aria-hidden="true">{order.down ? "↓" : "↑"}</span>}
    </button>
  )
}

export function SortBar({
  options,
  order,
  toggle,
}: {
  options: { key: string; caption: string }[]
  order: Order | null
  toggle: (key: string) => void
}) {
  if (options.length === 0) {
    return null
  }

  return (
    <div className="flex flex-wrap gap-x-4 gap-y-1 px-4 py-2 text-xs text-muted">
      {options.map((option) => (
        <SortCaption key={option.key} caption={option.caption} name={option.key} order={order} toggle={toggle} />
      ))}
    </div>
  )
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
      <dt className="text-xs text-muted">{column.caption}</dt>
      <dd className="min-w-0 text-sm text-ink">{value}</dd>
    </Fragment>
  )
}
