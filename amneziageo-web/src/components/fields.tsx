import type { ReactNode } from "react"
import { field, label } from "@/components/styles"

export function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="border-t border-line pt-3 first:border-t-0 first:pt-0">
      <div className="text-xs font-medium tracking-wide text-muted uppercase">{title}</div>
      <div className="mt-2 grid grid-cols-2 gap-3">{children}</div>
    </div>
  )
}

export function Line({
  id,
  caption,
  value,
  onChange,
  wide = false,
}: {
  id: string
  caption: string
  value: string
  onChange: (value: string) => void
  wide?: boolean
}) {
  return (
    <div className={wide ? "col-span-2" : ""}>
      <label className={label} htmlFor={id}>
        {caption}
      </label>
      <input id={id} value={value} onChange={(e) => onChange(e.target.value)} className={`mt-1 ${field}`} />
    </div>
  )
}

export function Count({
  id,
  caption,
  value,
  onChange,
}: {
  id: string
  caption: string
  value: number
  onChange: (value: number) => void
}) {
  return (
    <div>
      <label className={label} htmlFor={id}>
        {caption}
      </label>
      <input
        id={id}
        type="number"
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
        className={`mt-1 ${field}`}
      />
    </div>
  )
}

export function Flag({
  id,
  caption,
  value,
  onChange,
}: {
  id: string
  caption: string
  value: boolean
  onChange: (value: boolean) => void
}) {
  return (
    <label className="flex items-center gap-2 text-sm text-muted" htmlFor={id}>
      <input
        id={id}
        type="checkbox"
        checked={value}
        onChange={(e) => onChange(e.target.checked)}
        className="size-4 accent-brand"
      />
      {caption}
    </label>
  )
}

