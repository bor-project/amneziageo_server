import type { KeyboardEvent, ReactNode, Ref } from "react"
import { selectsAll, selectText } from "@/select"

export function TextBlock({
  className,
  children,
  ref,
}: {
  className: string
  children: ReactNode
  ref?: Ref<HTMLPreElement>
}) {
  return (
    <pre ref={ref} tabIndex={0} onKeyDown={own} className={className}>
      {children}
    </pre>
  )
}

function own(event: KeyboardEvent<HTMLPreElement>) {
  if (selectsAll(event)) {
    event.preventDefault()
    selectText(event.currentTarget)
  }
}
