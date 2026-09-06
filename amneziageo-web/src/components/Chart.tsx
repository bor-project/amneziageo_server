import { average } from "@/format"

export interface Trace {
  values: number[]
  tone: string
  filled?: boolean
}

const box = 40

export function Sparkline({
  traces,
  mean = false,
  height = "h-16",
}: {
  traces: Trace[]
  mean?: boolean
  height?: string
}) {
  const top = Math.max(1, ...traces.flatMap((trace) => trace.values)) * 1.15

  return (
    <svg viewBox={`0 0 100 ${box}`} preserveAspectRatio="none" className={`w-full ${height}`}>
      {traces.map((trace) => (
        <Line key={trace.tone} trace={trace} top={top} />
      ))}
      {mean && traces.length > 0 && <Mean trace={traces[0]} top={top} />}
    </svg>
  )
}

function Line({ trace, top }: { trace: Trace; top: number }) {
  const spots = points(trace.values, top)
  if (spots.length === 0) {
    return null
  }

  return (
    <g className={trace.tone}>
      {trace.filled !== false && (
        <polygon points={`0,${box} ${spots} 100,${box}`} fill="currentColor" opacity="0.12" />
      )}
      <polyline
        points={spots}
        fill="none"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinejoin="round"
        vectorEffect="non-scaling-stroke"
      />
    </g>
  )
}

function Mean({ trace, top }: { trace: Trace; top: number }) {
  if (trace.values.length === 0) {
    return null
  }

  const y = box - (average(trace.values) / top) * box

  return (
    <line
      x1="0"
      x2="100"
      y1={y}
      y2={y}
      className={trace.tone}
      stroke="currentColor"
      strokeWidth="1"
      strokeDasharray="4 4"
      opacity="0.5"
      vectorEffect="non-scaling-stroke"
    />
  )
}

function points(values: number[], top: number): string {
  if (values.length < 2) {
    return ""
  }

  return values
    .map((value, index) => `${(index / (values.length - 1)) * 100},${box - (value / top) * box}`)
    .join(" ")
}
