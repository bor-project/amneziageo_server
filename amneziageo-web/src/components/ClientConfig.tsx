import { useRef, useState } from "react"
import { Link } from "react-router-dom"
import { useQuery } from "@tanstack/react-query"
import { useClientConfig } from "@/api/clients"
import type { Miss } from "@/api/clients"
import { scopes } from "@/api/scopes"
import { Caret } from "@/components/Glyph"
import { card, chip, quiet } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

type Kind = "subscription" | "file" | "link"

interface Picture {
  url: string
  modules: number
}

interface Missing {
  says: string
  to: string | null
}

const kinds: { kind: Kind; label: TextKey; tail: string }[] = [
  { kind: "subscription", label: "clients.qrSubscription", tail: "-subscription.png" },
  { kind: "file", label: "clients.qrFile", tail: ".conf" },
  { kind: "link", label: "clients.qrLink", tail: "-link.png" },
]

const misses: Record<Miss, TextKey> = {
  off: "clients.subscriptionOff",
  "no-id": "clients.subscriptionNoId",
  "no-key": "clients.subscriptionNoKey",
}

export function ClientConfig({ id, editable }: { id: number; editable: boolean }) {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const config = useClientConfig(id)
  const words: Record<Kind, string> = {
    subscription: config.data?.subscription ?? "",
    file: config.data?.text ?? "",
    link: config.data?.link ?? "",
  }
  const pictures = useQuery({
    queryKey: ["client-qr", id, words.file, words.link, words.subscription],
    queryFn: async () => ({
      subscription: words.subscription.length > 0 ? await draw(words.subscription) : null,
      file: await draw(words.file),
      link: await draw(words.link),
    }),
    enabled: words.file.length > 0,
  })
  const name = config.data?.fileName ?? "client.conf"
  const stem = name.endsWith(".conf") ? name.slice(0, -".conf".length) : name
  const miss = config.data?.subscriptionMiss ?? ""
  const offered = kinds.filter((one) => words[one.kind].length > 0 || (one.kind === "subscription" && miss !== ""))

  function missing(kind: Kind): Missing | null {
    if (kind !== "subscription" || miss === "" || words.subscription.length > 0) {
      return null
    }

    if (miss === "off") {
      return { says: t(misses[miss]), to: holds(user, scopes.manageAccess) ? "/settings/subscriptions" : null }
    }

    return { says: t(misses[miss]), to: miss === "no-id" && editable ? `/connections/clients/${id}/edit` : null }
  }

  return (
    <div className="flex flex-col gap-4">
      {offered.map((one) => (
        <Sheet
          key={one.kind}
          caption={t(one.label)}
          text={words[one.kind]}
          picture={pictures.data?.[one.kind] ?? null}
          name={one.kind === "file" ? name : stem + one.tail}
          plain={one.kind === "file"}
          missing={missing(one.kind)}
          t={t}
        />
      ))}
    </div>
  )
}

function Sheet({
  caption,
  text,
  picture,
  name,
  plain,
  missing,
  t,
}: {
  caption: string
  text: string
  picture: Picture | null
  name: string
  plain: boolean
  missing: Missing | null
  t: Text
}) {
  const [open, setOpen] = useState(true)
  const [taken, setTaken] = useState("")
  const timer = useRef(0)
  const room = Math.min(440, Math.max(280, 3 * (picture?.modules ?? 0)))

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
      const url = URL.createObjectURL(new Blob([text], { type: "text/plain" }))
      save(name, url)
      URL.revokeObjectURL(url)
    } else if (picture !== null) {
      save(name, picture.url)
    }
  }

  return (
    <div className={card}>
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="flex w-full items-center gap-2 px-4 py-3 text-left text-sm text-ink"
      >
        <Caret open={open} />
        <span className="truncate">{caption}</span>
      </button>

      {open && missing !== null && (
        <div className="flex flex-wrap items-center gap-3 border-t border-line px-4 py-4 text-sm text-muted">
          {missing.says}
          {missing.to !== null && (
            <Link to={missing.to} className="text-brand-ink hover:text-brand-lit">
              {t("action.settings")}
            </Link>
          )}
        </div>
      )}

      {open && missing === null && (
        <div className="flex flex-col items-center gap-3 border-t border-line px-4 py-4">
          <div className="flex min-w-0 items-center gap-2 self-start">
            <span className={chip}>{caption}</span>
            <button
              type="button"
              title={t("action.copyText")}
              aria-label={t("action.copyText")}
              onClick={() => void put("text")}
              className={`${quiet} ${taken === "text" ? "text-good" : ""}`}
            >
              {taken === "text" ? <Tick /> : <Papers />}
            </button>
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
          </div>

          {picture === null ? (
            <div
              style={{ width: room, height: room }}
              className="flex items-center justify-center rounded border border-line p-4 text-center text-sm text-muted"
            >
              {t("clients.qrTooBig")}
            </div>
          ) : (
            <img
              src={picture.url}
              alt={t("clients.qr")}
              className="rounded bg-white p-2 [image-rendering:pixelated]"
              width={room}
              height={room}
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

async function draw(text: string): Promise<Picture | null> {
  const qr = (await import("qrcode")).default
  const fit = (level: "M" | "L") => {
    try {
      return qr.create(text, { errorCorrectionLevel: level }).modules.size + 2
    } catch {
      return 0
    }
  }
  const strong = fit("M")
  const level = strong > 0 ? "M" : "L"
  const modules = strong > 0 ? strong : fit("L")
  if (modules === 0) {
    return null
  }

  return { url: await qr.toDataURL(text, { errorCorrectionLevel: level, margin: 1, scale: 8 }), modules }
}

async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text)

    return true
  } catch {
    return false
  }
}

async function copyImage(picture: Picture | null): Promise<boolean> {
  if (picture === null) {
    return false
  }

  try {
    const blob = await (await fetch(picture.url)).blob()
    await navigator.clipboard.write([new ClipboardItem({ [blob.type]: blob })])

    return true
  } catch {
    return false
  }
}

function save(name: string, url: string) {
  const link = document.createElement("a")
  link.href = url
  link.download = name
  link.click()
}
