import { useState } from "react"
import type { TrafficLimits } from "@/api/trafficLimits"
import { readTrafficLimits, trafficLimitsInput } from "@/components/trafficLimits"

export function useTrafficLimits(start: TrafficLimits | null) {
  const [original] = useState(start)
  const [input, setInput] = useState(() => trafficLimitsInput(start))
  const changed = JSON.stringify(input) !== JSON.stringify(trafficLimitsInput(original))
  const checked = readTrafficLimits(input, original)

  return {
    input,
    setInput,
    changed,
    value: changed ? checked.value : original,
    faults: checked.faults,
    valid: Object.keys(checked.faults).length === 0,
  }
}
