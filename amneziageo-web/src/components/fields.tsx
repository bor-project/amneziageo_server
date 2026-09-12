import { useState } from "react"
import type { KeyboardEvent, ReactNode } from "react"
import { card, field, label, note } from "@/components/styles"

export function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="border-t border-line pt-3 first:border-t-0 first:pt-0">
      <div className="text-xs font-medium tracking-wide text-muted uppercase">{title}</div>
      <div className="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-2">{children}</div>
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
  hint = "",
  fault = "",
  after,
}: {
  id: string
  caption: string
  value: string
  onChange: (value: string) => void
  wide?: boolean
  placeholder?: string
  hint?: string
  fault?: string
  after?: ReactNode
}) {
  const input = (
    <input
      id={id}
      value={value}
      placeholder={placeholder}
      onChange={(e) => onChange(e.target.value)}
      className={after === undefined ? `mt-1 ${field}` : `min-w-0 flex-1 ${field}`}
    />
  )

  return (
    <div className={wide ? "sm:col-span-2" : ""}>
      <label className={label} htmlFor={id}>
        {caption}
      </label>
      {after === undefined ? (
        input
      ) : (
        <div className="mt-1 flex gap-2">
          {input}
          {after}
        </div>
      )}
      {hint.length > 0 && <div className={note}>{hint}</div>}
      {fault.length > 0 && <div className="mt-1 text-xs text-alarm">{fault}</div>}
    </div>
  )
}

export function Regenerate({ title, onClick }: { title: string; onClick: () => void }) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      onClick={onClick}
      className="flex shrink-0 items-center rounded border border-line px-2.5 text-muted hover:bg-hover hover:text-ink"
    >
      <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.6">
        <path d="M20 12a8 8 0 1 1-2.34-5.66L20 8" strokeLinecap="round" strokeLinejoin="round" />
        <path d="M20 3v5h-5" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    </button>
  )
}

export function Pick({
  id,
  caption,
  value,
  onChange,
  children,
  wide = false,
  hint = "",
  disabled = false,
}: {
  id: string
  caption: string
  value: string
  onChange: (value: string) => void
  children: ReactNode
  wide?: boolean
  hint?: string
  disabled?: boolean
}) {
  return (
    <div className={wide ? "sm:col-span-2" : ""}>
      <label className={label} htmlFor={id}>
        {caption}
      </label>
      <select
        id={id}
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        className={`mt-1 ${field}`}
      >
        {children}
      </select>
      {hint.length > 0 && <div className={note}>{hint}</div>}
    </div>
  )
}

export function Count({
  id,
  caption,
  value,
  onChange,
  hint = "",
}: {
  id: string
  caption: string
  value: number
  onChange: (value: number) => void
  hint?: string
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
      {hint.length > 0 && <div className={note}>{hint}</div>}
    </div>
  )
}

export function Flag({
  id,
  caption,
  value,
  onChange,
  hint = "",
}: {
  id: string
  caption: string
  value: boolean
  onChange: (value: boolean) => void
  hint?: string
}) {
  const box = (
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

  return hint.length > 0 ? (
    <div>
      {box}
      <div className={note}>{hint}</div>
    </div>
  ) : (
    box
  )
}


export function Switch({
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
    <div className="flex items-center gap-2">
      <label className="text-sm text-ink" htmlFor={id}>
        {caption}
      </label>
      <button
        id={id}
        type="button"
        role="switch"
        aria-checked={value}
        onClick={() => onChange(!value)}
        className={`relative h-5 w-9 shrink-0 rounded-full ${value ? "bg-brand" : "bg-line"}`}
      >
        <span
          className={`absolute top-[2px] size-4 rounded-full bg-surface ${value ? "left-[18px]" : "left-[2px]"}`}
        />
      </button>
    </div>
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
