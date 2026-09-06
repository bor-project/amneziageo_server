import { useEffect } from "react"
import type { ReactNode } from "react"
import { card } from "@/components/styles"

export function Modal({
  title,
  onClose,
  children,
  footer,
}: {
  title: string
  onClose: () => void
  children: ReactNode
  footer: ReactNode
}) {
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
      onMouseDown={onClose}
    >
      <div
        className={`w-full max-w-md p-5 shadow-xl ${card}`}
        onMouseDown={(event) => event.stopPropagation()}
      >
        <div className="text-base font-semibold text-ink">{title}</div>
        <div className="mt-4 flex flex-col gap-3">{children}</div>
        <div className="mt-5 flex justify-end gap-2">{footer}</div>
      </div>
    </div>
  )
}
