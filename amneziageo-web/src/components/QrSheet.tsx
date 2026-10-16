import { useRef, useState } from "react"
import type { ReactNode } from "react"
import { card, quiet } from "@/components/styles"
import type { Text } from "@/i18n"
import { copyImage, copyText, save, saveText } from "@/qr"
import type { Picture } from "@/qr"

// The browser copies a picture only on a page served over HTTPS or from localhost.
const pictures = window.isSecureContext && typeof ClipboardItem !== "undefined"

/**
 * A card of the export of a configuration: its caption, the buttons that copy and save it, and its QR code, or a note
 * in place of them.
 */
export function QrSheet({
  caption,
  text,
  picture,
  name,
  plain,
  note,
  t,
}: {
  caption: string
  text: string
  picture: Picture | null
  name: string
  plain: boolean
  note: ReactNode
  t: Text
}) {
  const [taken, setTaken] = useState("")
  const timer = useRef(0)

  async function put(what: "text" | "image") {
    const done = what === "text" ? await copyText(text) : await copyImage(picture)
    if (!done) {
      return
    }

    setTaken(what)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => setTaken(""), 1500)
  }

  function keep() {
    if (plain) {
      saveText(name, text)
    } else if (picture !== null) {
      save(name, picture.url)
    }
  }

  return (
    <div className={card}>
      <div className="flex min-h-12 items-center gap-2 border-b border-line px-4 py-2">
        <span className="min-w-0 flex-1 truncate text-sm font-medium text-ink">{caption}</span>
        {note === null && (
          <>
            <button
              type="button"
              title={t("action.copyText")}
              aria-label={t("action.copyText")}
              onClick={() => void put("text")}
              className={`${quiet} ${taken === "text" ? "text-good" : ""}`}
            >
              {taken === "text" ? <Tick /> : <Papers />}
            </button>
            {pictures && (
              <button
                type="button"
                title={t("action.copyImage")}
                aria-label={t("action.copyImage")}
                onClick={() => void put("image")}
                disabled={picture === null}
                className={`${quiet} ${taken === "image" ? "text-good" : ""}`}
              >
                {taken === "image" ? <Tick /> : <Frame />}
              </button>
            )}
            <button
              type="button"
              title={t("clients.download")}
              aria-label={t("clients.download")}
              onClick={keep}
              disabled={!plain && picture === null}
              className={quiet}
            >
              <Arrow />
            </button>
          </>
        )}
      </div>

      {note !== null && <div className="flex flex-wrap items-center gap-3 px-4 py-6 text-sm text-muted">{note}</div>}

      {note === null && (
        <div className="flex justify-center p-4">
          {picture === null ? (
            <div className="flex aspect-square w-full max-w-80 items-center justify-center rounded border border-line p-4 text-center text-sm text-muted">
              {t("clients.qrTooBig")}
            </div>
          ) : (
            <img
              src={picture.url}
              alt={t("clients.qr")}
              className="aspect-square w-full max-w-80 rounded bg-white p-2 [image-rendering:pixelated]"
            />
          )}
        </div>
      )}
    </div>
  )
}

function Tick() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden>
      <path d="m5 12.5 4.5 4.5L19 7" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

function Papers() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden>
      <rect x="9" y="9" width="11" height="11" rx="2" />
      <path d="M5 15V5a2 2 0 0 1 2-2h8" />
    </svg>
  )
}

function Frame() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <circle cx="8.5" cy="9.5" r="1.5" />
      <path d="m4 17 5-5 4 4 3-3 4 4" />
    </svg>
  )
}

function Arrow() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden>
      <path d="M12 4v11m-4-4 4 4 4-4" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M5 19h14" strokeLinecap="round" />
    </svg>
  )
}
