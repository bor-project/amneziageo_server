import { useState } from "react"
import type { TrafficLimits } from "@/api/trafficLimits"
import { readTrafficLimits, trafficLimitsInput } from "@/components/trafficLimits"
import type { TrafficLimitsInput } from "@/components/trafficLimits"

export function useTrafficLimits(start: TrafficLimits | null) {
  const [edited, setEdited] = useState<{ original: TrafficLimits | null; input: TrafficLimitsInput } | null>(null)
  const original = edited === null ? start : edited.original
  const input = edited === null ? trafficLimitsInput(start) : edited.input
  const changed = edited !== null
  const checked = readTrafficLimits(input, original)

  function setInput(next: TrafficLimitsInput) {
    setEdited(JSON.stringify(next) === JSON.stringify(trafficLimitsInput(start)) ? null : { original, input: next })
  }

  return {
    input,
    setInput,
    changed,
    value: changed ? checked.value : start,
    faults: checked.faults,
    valid: Object.keys(checked.faults).length === 0,
  }
}
