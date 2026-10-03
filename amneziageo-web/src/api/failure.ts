import { useState } from "react"

// The failure a query met last, kept until it gets an answer. A query that never got one starts every new try afresh,
// so a list read every two seconds would flip between loading and its failure.
export function useFailure(query: { error: Error | null; isSuccess: boolean }): Error | null {
  const [held, setHeld] = useState<Error | null>(null)
  const now = query.isSuccess ? null : (query.error ?? held)

  if (now !== held) {
    setHeld(now)
  }

  return now
}
