import { useState } from "react"
import type { KeyboardEvent, ReactNode } from "react"
import { card, field, label, note } from "@/components/styles"

export function Part({ title, children }: { title: ReactNode; children: ReactNode }) {
  return (
    <div className={`flex flex-col gap-3.5 p-4.5 ${card}`}>
      <div className="text-sm font-semibold text-ink-soft">{title}</div>
      <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">{children}</div>
    </div>
  )
}

export function Box({ caption, children }: { caption: string; children: ReactNode }) {
  return (
    <div className={`p-4 ${card}`}>
      <div className="text-xs text-faint">{caption}</div>
      <div className="mt-2">{children}</div>
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
  caption: ReactNode
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

export function Regenerate({
  title,
  onClick,
  disabled = false,
}: {
  title: string
  onClick: () => void
  disabled?: boolean
}) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      onClick={onClick}
      disabled={disabled}
      className="flex shrink-0 items-center rounded border border-line px-2.5 text-muted hover:bg-hover hover:text-ink disabled:opacity-50"
    >
      <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden>
        <rect x="4" y="4" width="16" height="16" rx="3.5" />
        <circle cx="8.75" cy="8.75" r="1.25" fill="currentColor" stroke="none" />
        <circle cx="15.25" cy="8.75" r="1.25" fill="currentColor" stroke="none" />
        <circle cx="12" cy="12" r="1.25" fill="currentColor" stroke="none" />
        <circle cx="8.75" cy="15.25" r="1.25" fill="currentColor" stroke="none" />
        <circle cx="15.25" cy="15.25" r="1.25" fill="currentColor" stroke="none" />
      </svg>
    </button>
  )
}

export function Help({ text }: { text?: string }) {
  return (
    <span className="inline-flex items-center" title={text}>
      <svg viewBox="0 0 24 24" className="size-3.5 shrink-0 text-muted" fill="none" stroke="currentColor" strokeWidth="1.6">
        <circle cx="12" cy="12" r="9" />
        <path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3" strokeLinecap="round" />
        <path d="M12 17h.01" strokeLinecap="round" />
      </svg>
    </span>
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
  caption: ReactNode
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
  unset = "",
}: {
  id: string
  caption: ReactNode
  value: number
  onChange: (value: number) => void
  hint?: string
  unset?: string
}) {
  return (
    <div>
      <label className={label} htmlFor={id}>
        {caption}
      </label>
      <input
        id={id}
        type="number"
        value={unset.length > 0 && value === 0 ? "" : value}
        placeholder={unset}
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
  caption: ReactNode
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
  caption: ReactNode
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

export function Knob({
  value,
  onChange,
  title,
  disabled = false,
}: {
  value: boolean
  onChange: (value: boolean) => void
  title: string
  disabled?: boolean
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={value}
      aria-label={title}
      title={title}
      disabled={disabled}
      onClick={() => onChange(!value)}
      className={`relative block h-5 w-9 shrink-0 rounded-full disabled:opacity-60 ${value ? "bg-brand" : "bg-line"}`}
    >
      <span
        className={`absolute top-[2px] size-4 rounded-full bg-surface ${value ? "left-[18px]" : "left-[2px]"}`}
      />
    </button>
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
