import { useEffect } from "react"
import type { ReactNode } from "react"
import { card } from "@/components/styles"

export function Modal({
  title,
  onClose,
  children,
  footer,
  wide = false,
}: {
  title: string
  onClose: () => void
  children: ReactNode
  footer: ReactNode
  wide?: boolean
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
        className={`w-full ${wide ? "max-w-3xl" : "max-w-md"} p-5 shadow-xl ${card}`}
        onMouseDown={(event) => event.stopPropagation()}
      >
        <div className="text-base font-semibold text-ink">{title}</div>
        <div className="mt-4 flex max-h-[70vh] flex-col gap-3 overflow-y-auto">{children}</div>
        <div className="mt-5 flex justify-end gap-2">{footer}</div>
      </div>
    </div>
  )
}
