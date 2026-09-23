import type { ReactNode } from "react"

export type GlyphName =
  | "shield"
  | "devices"
  | "layout"
  | "fork"
  | "list"
  | "flask"
  | "route"
  | "globe"
  | "map"
  | "wall"
  | "server"
  | "lock"
  | "feed"
  | "people"
  | "pulse"

const drawings: Record<GlyphName, ReactNode> = {
  shield: <path d="M12 3l7 3v5.5c0 4.3-2.9 7.9-7 9.5-4.1-1.6-7-5.2-7-9.5V6l7-3Z" />,
  devices: (
    <>
      <rect x="2.5" y="5" width="11.5" height="8" rx="1.5" />
      <path d="M1.5 16.5h14" />
      <rect x="16.5" y="8" width="5.5" height="12" rx="1.2" />
    </>
  ),
  layout: (
    <>
      <rect x="3.5" y="4" width="17" height="16" rx="2" />
      <path d="M3.5 9h17M9.5 9v11" />
    </>
  ),
  fork: <path d="M12 20v-5l-6-6V4M12 15l6-6V4M3.5 6.5 6 4l2.5 2.5M15.5 6.5 18 4l2.5 2.5" />,
  list: (
    <>
      <path d="M9 6.5h11M9 12h11M9 17.5h11" />
      <path d="M4.5 6.5h.01M4.5 12h.01M4.5 17.5h.01" strokeWidth="2.6" />
    </>
  ),
  flask: (
    <>
      <path d="M9 3.5h6M10 3.5v5.2L4.8 17.6a2 2 0 0 0 1.7 2.9h11a2 2 0 0 0 1.7-2.9L14 8.7V3.5" />
      <path d="M7.2 14.5h9.6" />
    </>
  ),
  route: (
    <>
      <circle cx="6" cy="18" r="2.5" />
      <circle cx="18" cy="6" r="2.5" />
      <path d="M8.5 18H15a3 3 0 0 0 0-6H9a3 3 0 0 1 0-6h6.5" />
    </>
  ),
  globe: (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M3 12h18M12 3c2.4 2.5 3.6 5.5 3.6 9s-1.2 6.5-3.6 9c-2.4-2.5-3.6-5.5-3.6-9S9.6 5.5 12 3Z" />
    </>
  ),
  map: (
    <>
      <path d="M3.5 6.5 9 4l6 2.5L20.5 4v13.5L15 20l-6-2.5-5.5 2.5Z" />
      <path d="M9 4v13.5M15 6.5V20" />
    </>
  ),
  wall: (
    <>
      <rect x="3" y="5" width="18" height="14" rx="1.5" />
      <path d="M3 9.7h18M3 14.3h18M9 5v4.7M15 5v4.7M6 9.7v4.6M12 9.7v4.6M18 9.7v4.6M9 14.3V19M15 14.3V19" />
    </>
  ),
  server: (
    <>
      <rect x="3.5" y="4" width="17" height="7" rx="1.5" />
      <rect x="3.5" y="13" width="17" height="7" rx="1.5" />
      <path d="M7 7.5h.01M7 16.5h.01" strokeWidth="2.6" />
    </>
  ),
  lock: (
    <>
      <rect x="5" y="10.5" width="14" height="10" rx="2" />
      <path d="M8 10.5v-3a4 4 0 0 1 8 0v3M12 14.5v2" />
    </>
  ),
  feed: (
    <>
      <path d="M5 11a8 8 0 0 1 8 8M5 5a14 14 0 0 1 14 14" />
      <path d="M5.5 18.5h.01" strokeWidth="3" />
    </>
  ),
  people: (
    <>
      <circle cx="9" cy="8" r="3.5" />
      <path d="M2.5 20a6.5 6.5 0 0 1 13 0M16 4.6a3.5 3.5 0 0 1 0 6.8M17.5 14.2c2.3.8 4 3 4 5.8" />
    </>
  ),
  pulse: <path d="M3 12h4l2.5-6 5 12 2.5-6h4" />,
}

export function Caret({ open }: { open: boolean }) {
  return (
    <svg
      viewBox="0 0 24 24"
      className={`size-4 shrink-0 text-faint ${open ? "rotate-90" : ""}`}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      <path d="m9 6 6 6-6 6" />
    </svg>
  )
}

export function Glyph({ name }: { name: GlyphName }) {
  return (
    <svg
      viewBox="0 0 24 24"
      className="size-5"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      {drawings[name]}
    </svg>
  )
}
