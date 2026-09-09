import { useState } from "react"
import type { KeyboardEvent, ReactNode } from "react"
import { card, field, label } from "@/components/styles"

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
  placeholder = "",
}: {
  id: string
  caption: string
  value: string
  onChange: (value: string) => void
  wide?: boolean
  placeholder?: string
}) {
  return (
    <div className={wide ? "col-span-2" : ""}>
      <label className={label} htmlFor={id}>
        {caption}
      </label>
      <input
        id={id}
        value={value}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        className={`mt-1 ${field}`}
      />
    </div>
  )
}

export function Pick({
  id,
  caption,
  value,
  onChange,
  children,
  wide = false,
}: {
  id: string
  caption: string
  value: string
  onChange: (value: string) => void
  children: ReactNode
  wide?: boolean
}) {
  return (
    <div className={wide ? "col-span-2" : ""}>
      <label className={label} htmlFor={id}>
        {caption}
      </label>
      <select id={id} value={value} onChange={(e) => onChange(e.target.value)} className={`mt-1 ${field}`}>
        {children}
      </select>
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


export function Row({ id, caption, children }: { id: string; caption: string; children: ReactNode }) {
  return (
    <div className="grid items-center gap-2 border-t border-line py-3 first:border-t-0 first:pt-0 sm:grid-cols-2 sm:gap-6">
      <label className="text-sm font-medium text-ink" htmlFor={id}>
        {caption}
      </label>
      {children}
    </div>
  )
}

export function Multi({
  id,
  value,
  offers,
  placeholder,
  onChange,
}: {
  id: string
  value: string[]
  offers: string[]
  placeholder: string
  onChange: (value: string[]) => void
}) {
  const [text, setText] = useState("")
  const [open, setOpen] = useState(false)
  const left = offers.filter(
    (offer) => !value.includes(offer) && offer.toLowerCase().includes(text.trim().toLowerCase()),
  )

  function add(line: string) {
    const found = line
      .split(";")
      .map((item) => item.trim())
      .filter((item) => item.length > 0 && !value.includes(item))

    setText("")
    if (found.length > 0) {
      onChange([...value, ...found])
    }
  }

  function press(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter" || event.key === ";") {
      event.preventDefault()
      add(text)

      return
    }

    if (event.key === "Backspace" && text.length === 0 && value.length > 0) {
      onChange(value.slice(0, -1))
    }
  }

  return (
    <div className="relative">
      <div className={`flex flex-wrap items-center gap-1 ${field}`}>
        {value.map((item) => (
          <span key={item} className="flex items-center gap-1 rounded bg-hover px-2 py-0.5 text-sm text-ink">
            {item}
            <button
              type="button"
              className="text-muted hover:text-alarm"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => onChange(value.filter((one) => one !== item))}
            >
              &times;
            </button>
          </span>
        ))}
        <input
          id={id}
          className="min-w-32 flex-1 bg-transparent text-sm text-ink outline-none"
          value={text}
          placeholder={value.length === 0 ? placeholder : ""}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={press}
          onFocus={() => setOpen(true)}
          onBlur={() => add(text)}
        />
      </div>

      {open && left.length > 0 && (
        <>
          <div className="fixed inset-0 z-30" onMouseDown={(e) => { e.preventDefault(); setOpen(false) }} />
          <div className={`absolute z-40 mt-1 flex w-full flex-col py-1 shadow-lg ${card}`}>
            {left.map((offer) => (
              <button
                key={offer}
                type="button"
                className="px-3 py-2 text-left text-sm text-muted hover:bg-hover hover:text-brand-ink"
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => add(offer)}
              >
                {offer}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
