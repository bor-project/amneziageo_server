import { useEffect, useId, useLayoutEffect, useRef, useState } from "react"
import type { KeyboardEvent, MouseEvent, PointerEvent, ReactNode } from "react"
import { usePanelDocs } from "@/components/docs"
import { Help } from "@/components/fields"
import { menu } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

// What reaches the AmneziaGeo app alone, by the place that says so.
export type AppOnly =
  | "webSocket"
  | "services"
  | "presets"
  | "presetsOnly"
  | "presetList"
  | "subscription"
  | "subscriptions"
  | "clientUpdates"
  | "subscriptionQr"
  | "signal"
  | "geo"
  | "resolver"

// What other clients get, where it is more than the file of the configuration.
const apart: Partial<Record<AppOnly, TextKey>> = {
  geo: "appOnly.geoOthers",
  resolver: "appOnly.resolverOthers",
}

const width = 400

// How long the window stays after the pointer leaves, so the pointer can reach it.
const linger = 200

// The sign of AmneziaGeo: the white power glyph on the accent disc.
export function AppSign({ className }: { className: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden>
      <circle cx="12" cy="12" r="11.6" fill="#2A6FDB" />
      <path
        fill="#fff"
        transform="translate(4.5 4.5) scale(0.625)"
        d="M13 3h-2v10h2V3zm4.83 2.17l-1.42 1.42C17.99 7.86 19 9.81 19 12c0 3.87-3.13 7-7 7s-7-3.13-7-7c0-2.19 1.01-4.14 2.58-5.42L6.17 5.17C4.23 6.82 3 9.26 3 12c0 4.97 4.03 9 9 9s9-4.03 9-9c0-2.74-1.23-5.18-3.17-6.83z"
      />
    </svg>
  )
}

// The sign beside what only the AmneziaGeo app takes, with what the app does with it in a window behind it: the
// pointer resting on the sign opens the window, a press holds it open. A large sign stands beside the title of a page.
export function AppMark({ what, large = false }: { what: AppOnly; large?: boolean }) {
  const t = useText()
  const link = useDocs()
  const id = useId()
  const sign = useRef<HTMLButtonElement>(null)
  const pop = useRef<HTMLSpanElement>(null)
  const leave = useRef<number | null>(null)
  const [spot, setSpot] = useState<{ top: number; left: number } | null>(null)
  const [held, setHeld] = useState(false)
  const title = t("appOnly.title")

  function place() {
    const box = sign.current?.getBoundingClientRect()
    if (box === undefined) {
      return
    }

    const room = Math.min(width, window.innerWidth - 16)
    setSpot({ top: box.bottom + 4, left: Math.max(8, Math.min(box.left - 8, window.innerWidth - room - 8)) })
  }

  function close() {
    setSpot(null)
    setHeld(false)
  }

  function stay() {
    if (leave.current !== null) {
      window.clearTimeout(leave.current)
      leave.current = null
    }
  }

  function enter(event: PointerEvent) {
    stay()
    if (event.pointerType === "mouse" && spot === null) {
      place()
    }
  }

  function away(event: PointerEvent) {
    if (event.pointerType !== "mouse" || held) {
      return
    }

    stay()
    leave.current = window.setTimeout(close, linger)
  }

  function press(event: MouseEvent<HTMLButtonElement>) {
    event.preventDefault()
    event.stopPropagation()
    if (held) {
      close()

      return
    }

    place()
    setHeld(true)
  }

  function key(event: KeyboardEvent) {
    if (event.key === "Escape" && spot !== null) {
      event.stopPropagation()
      close()
      sign.current?.focus()
    }
  }

  // A window with no room under the sign stands over it.
  useLayoutEffect(() => {
    const box = pop.current?.getBoundingClientRect()
    const anchor = sign.current?.getBoundingClientRect()
    if (spot === null || box === undefined || anchor === undefined) {
      return
    }

    if (box.bottom > window.innerHeight - 8 && anchor.top - 4 - box.height >= 8) {
      setSpot({ top: anchor.top - 4 - box.height, left: spot.left })
    }
  }, [spot])

  useEffect(() => stay, [])

  return (
    <span className="inline-flex shrink-0 align-middle" onPointerEnter={enter} onPointerLeave={away} onKeyDown={key}>
      <button
        ref={sign}
        type="button"
        aria-label={title}
        aria-expanded={spot !== null}
        aria-controls={spot !== null ? id : undefined}
        onClick={press}
        className="flex items-center rounded-full"
      >
        <AppSign className={large ? "size-5" : "size-3.5"} />
      </button>
      {spot !== null && (
        <>
          {held && <span className="fixed inset-0 z-30" onMouseDown={close} />}
          <span
            ref={pop}
            id={id}
            role="dialog"
            aria-label={title}
            style={{ top: spot.top, left: spot.left, maxWidth: Math.min(width, window.innerWidth - 16) }}
            className={`fixed z-40 flex w-max flex-col gap-1.5 px-3 py-2.5 text-left text-[13px] font-normal whitespace-normal text-body ${menu}`}
          >
            <span className="flex items-center gap-1.5 font-medium text-ink">
              <AppSign className="size-4 shrink-0" />
              {title}
            </span>
            <span>{t(`appOnly.${what}`)}</span>
            <span className="text-muted">{t(apart[what] ?? "appOnly.others")}</span>
            <a href={link} target="_blank" rel="noreferrer" className="text-brand-ink hover:text-brand-lit">
              {t("appOnly.more")}
            </a>
          </span>
        </>
      )}
    </span>
  )
}

// A caption with the sign after it and, when it has one, its short note behind a question mark.
export function AppCaption({ caption, what, hint }: { caption: ReactNode; what: AppOnly; hint?: string }) {
  return (
    <span className="flex items-center gap-1">
      {caption}
      {hint !== undefined && <Help text={hint} />}
      <AppMark what={what} />
    </span>
  )
}

// The part of the site about the sign, on the page of the panel of the way it runs and in its language.
function useDocs(): string {
  return usePanelDocs("index", "значок-amneziageo", "amneziageo-mark")
}
