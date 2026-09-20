import { quiet } from "@/components/styles"

export function Move({
  first,
  last,
  upTitle,
  downTitle,
  onMove,
}: {
  first: boolean
  last: boolean
  upTitle: string
  downTitle: string
  onMove: (up: boolean) => void
}) {
  return (
    <>
      <button
        type="button"
        title={upTitle}
        aria-label={upTitle}
        disabled={first}
        onClick={() => onMove(true)}
        className={`disabled:opacity-30 ${quiet}`}
      >
        <Arrow up />
      </button>
      <button
        type="button"
        title={downTitle}
        aria-label={downTitle}
        disabled={last}
        onClick={() => onMove(false)}
        className={`disabled:opacity-30 ${quiet}`}
      >
        <Arrow />
      </button>
    </>
  )
}

function Arrow({ up = false }: { up?: boolean }) {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path
        d={up ? "M12 19V5M6 11l6-6 6 6" : "M12 5v14M6 13l6 6 6-6"}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}
