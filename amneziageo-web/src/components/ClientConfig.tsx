import { useState } from "react"
import { useQuery } from "@tanstack/react-query"
import { useClientConfig } from "@/api/clients"
import { Modal } from "@/components/Modal"
import { primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

type Kind = "file" | "link"

interface Picture {
  url: string
  modules: number
}

const kinds: { kind: Kind; label: TextKey }[] = [
  { kind: "file", label: "clients.qrFile" },
  { kind: "link", label: "clients.qrLink" },
]

const tab = "-mb-px border-b-2 px-1 pb-2 text-sm"
const chosen = "border-brand font-medium text-brand-ink"
const plain = "border-transparent text-muted hover:text-brand-ink"

export function ClientConfig({ id, title, onClose }: { id: number; title: string; onClose: () => void }) {
  const t = useText()
  const config = useClientConfig(id)
  const [picked, setPicked] = useState<Kind | null>(null)
  const text = config.data?.text ?? ""
  const link = config.data?.link ?? ""
  const pictures = useQuery({
    queryKey: ["client-qr", id, text, link],
    queryFn: async () => ({ file: await draw(text), link: await draw(link) }),
    enabled: text.length > 0,
  })
  const file = pictures.data?.file
  const packed = pictures.data?.link
  const kind = picked ?? (file === null && packed ? "link" : "file")
  const picture = kind === "file" ? file : packed
  const room = Math.min(440, Math.max(280, 3 * Math.max(file?.modules ?? 0, packed?.modules ?? 0)))

  return (
    <Modal
      title={title}
      onClose={onClose}
      wide
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("clients.close")}
          </button>
          <button
            type="button"
            onClick={() => save(config.data?.fileName ?? "client.conf", text)}
            disabled={text.length === 0}
            className={primary}
          >
            {t("clients.download")}
          </button>
        </>
      }
    >
      <div className="grid gap-4 sm:grid-cols-[auto_1fr]">
        <div className="flex flex-col items-center gap-3">
          <div className="flex gap-6 self-stretch border-b border-line">
            {kinds.map((one) => (
              <button
                key={one.kind}
                type="button"
                onClick={() => setPicked(one.kind)}
                className={`${tab} ${one.kind === kind ? chosen : plain}`}
              >
                {t(one.label)}
              </button>
            ))}
          </div>

          {picture === null && (
            <div
              style={{ width: room, height: room }}
              className="flex items-center justify-center rounded border border-line p-4 text-center text-sm text-muted"
            >
              {t("clients.qrTooBig")}
            </div>
          )}
          {picture && (
            <img
              src={picture.url}
              alt={t("clients.qr")}
              className="rounded bg-white p-2 [image-rendering:pixelated]"
              width={room}
              height={room}
            />
          )}
        </div>

        <pre
          className={`max-h-80 overflow-auto rounded border border-line bg-canvas p-3 text-xs text-ink ${kind === "link" ? "break-all whitespace-pre-wrap" : ""}`}
        >
          {kind === "file" ? text : link}
        </pre>
      </div>
    </Modal>
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

function save(name: string, text: string) {
  const blob = new Blob([text], { type: "text/plain" })
  const url = URL.createObjectURL(blob)
  const link = document.createElement("a")
  link.href = url
  link.download = name
  link.click()
  URL.revokeObjectURL(url)
}
