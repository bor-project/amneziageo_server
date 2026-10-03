import { useEffect, useId, useRef } from "react"
import type { ReactNode } from "react"
import { card } from "@/components/styles"

// A question over the page, closed by its buttons, by Escape or by a click beside it.
export function Dialog({
  title,
  children,
  actions,
  onClose,
}: {
  title: string
  children?: ReactNode
  actions: ReactNode
  onClose: () => void
}) {
  const heading = useId()
  const panel = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const before = document.activeElement instanceof HTMLElement ? document.activeElement : null
    panel.current?.querySelector("button")?.focus()

    return () => before?.focus()
  }, [])

  useEffect(() => {
    const escape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose()
      }
    }

    window.addEventListener("keydown", escape)

    return () => window.removeEventListener("keydown", escape)
  }, [onClose])

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose()
        }
      }}
    >
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-labelledby={heading}
        className={`flex w-full max-w-md flex-col gap-3 p-5 shadow-[var(--shade)] ${card}`}
      >
        <h2 id={heading} className="text-base font-semibold text-ink">
          {title}
        </h2>
        {children !== undefined && <div className="text-sm text-body">{children}</div>}
        <div className="mt-2 flex flex-wrap justify-end gap-2">{actions}</div>
      </div>
    </div>
  )
}
