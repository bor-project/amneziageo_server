import { Fragment } from "react"
import type { ReactNode } from "react"
import { roomyQuery, useAbove } from "@/theme/width"

export interface Column<T> {
  key: string
  caption: string
  cell: (item: T, at: number) => ReactNode
  lead?: boolean
  tail?: boolean
  head?: string
  body?: string
}

export function Rows<T>({
  items,
  columns,
  keyOf,
}: {
  items: T[]
  columns: Column<T>[]
  keyOf: (item: T) => string | number
}) {
  const roomy = useAbove(roomyQuery)

  if (roomy) {
    return (
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="text-xs text-muted">
            <tr>
              {columns.map((column) => (
                <th key={column.key} className={`px-4 py-2 font-normal ${column.head ?? ""}`}>
                  {column.tail ? "" : column.caption}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {items.map((item, at) => (
              <tr key={keyOf(item)} className="border-t border-line">
                {columns.map((column) => (
                  <td key={column.key} className={`px-4 py-2 ${column.body ?? ""}`}>
                    {column.cell(item, at)}
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
      {items.map((item, at) => (
        <div key={keyOf(item)} className="border-t border-line px-4 py-3">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0 text-sm font-medium text-ink">{lead.cell(item, at)}</div>
            {tail && <div className="shrink-0">{tail.cell(item, at)}</div>}
          </div>

          <dl className="mt-2 grid grid-cols-[minmax(0,10rem)_minmax(0,1fr)] gap-x-3 gap-y-1">
            {rest.map((column) => told(column.cell(item, at), column))}
          </dl>
        </div>
      ))}
    </div>
  )
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
