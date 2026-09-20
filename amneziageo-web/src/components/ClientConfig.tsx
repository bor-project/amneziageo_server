import { useState } from "react"
import { useQuery } from "@tanstack/react-query"
import { useClientConfig } from "@/api/clients"
import { card, chip, quiet, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"

type Kind = "subscription" | "file" | "link"

interface Picture {
  url: string
  modules: number
}

const kinds: { kind: Kind; label: TextKey }[] = [
  { kind: "subscription", label: "clients.qrSubscription" },
  { kind: "file", label: "clients.qrFile" },
  { kind: "link", label: "clients.qrLink" },
]

export function ClientConfig({ id }: { id: number }) {
  const t = useText()
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
  const offered = kinds.filter((one) => words[one.kind].length > 0)

  return (
    <div className="flex flex-col gap-4">
      <div className={`flex items-center justify-between gap-4 px-4 py-3 ${card}`}>
        <div className="truncate font-mono text-xs text-mono">{config.data?.fileName ?? ""}</div>
        <button
          type="button"
          onClick={() => save(config.data?.fileName ?? "client.conf", words.file)}
          disabled={words.file.length === 0}
          className={secondary}
        >
          {t("clients.download")}
        </button>
      </div>

      {offered.map((one) => (
        <Sheet
          key={one.kind}
          caption={t(one.label)}
          text={words[one.kind]}
          picture={pictures.data?.[one.kind] ?? null}
          t={t}
        />
      ))}
    </div>
  )
}

function Sheet({ caption, text, picture, t }: { caption: string; picture: Picture | null; text: string; t: Text }) {
  const [open, setOpen] = useState(true)
  const [taken, setTaken] = useState("")
  const room = Math.min(440, Math.max(280, 3 * (picture?.modules ?? 0)))

  async function put(what: "text" | "image") {
    const done = what === "text" ? await copyText(text) : await copyImage(picture)
    setTaken(done ? what : "")
  }

  return (
    <div className={card}>
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="flex w-full items-center gap-2 px-4 py-3 text-left text-sm text-ink"
      >
        <span className="text-[10px] text-faint" aria-hidden>
          {open ? "\u25be" : "\u25b8"}
        </span>
        <span className="truncate">{caption}</span>
      </button>

      {open && (
        <div className="flex flex-col items-center gap-3 border-t border-line px-4 py-4">
          <div className="flex items-center gap-2 self-start">
            <span className={chip}>{caption}</span>
            <button
              type="button"
              title={t("action.copyText")}
              aria-label={t("action.copyText")}
              onClick={() => void put("text")}
              className={`${quiet} ${taken === "text" ? "text-brand-ink" : ""}`}
            >
              <Papers />
            </button>
            <button
              type="button"
              title={t("action.copyImage")}
              aria-label={t("action.copyImage")}
              onClick={() => void put("image")}
              disabled={picture === null}
              className={`${quiet} ${taken === "image" ? "text-brand-ink" : ""}`}
            >
              <Frame />
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

function save(name: string, text: string) {
  const blob = new Blob([text], { type: "text/plain" })
  const url = URL.createObjectURL(blob)
  const link = document.createElement("a")
  link.href = url
  link.download = name
  link.click()
  URL.revokeObjectURL(url)
}
