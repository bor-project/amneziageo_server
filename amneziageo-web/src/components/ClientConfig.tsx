import { useQuery } from "@tanstack/react-query"
import { useClientConfig } from "@/api/clients"
import { Modal } from "@/components/Modal"
import { primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function ClientConfig({ id, title, onClose }: { id: number; title: string; onClose: () => void }) {
  const t = useText()
  const config = useClientConfig(id)
  const text = config.data?.text ?? ""
  const picture = useQuery({
    queryKey: ["client-qr", id, text.length],
    queryFn: async () => (await import("qrcode")).default.toDataURL(text, { margin: 1, width: 320 }),
    enabled: text.length > 0,
  })

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
      <div className="grid gap-4 sm:grid-cols-[280px_1fr]">
        <div className="flex items-start justify-center">
          {picture.data !== undefined && (
            <img src={picture.data} alt={t("clients.qr")} className="rounded bg-white p-2" width={280} height={280} />
          )}
        </div>

        <pre className="max-h-80 overflow-auto rounded border border-line bg-canvas p-3 text-xs text-ink">{text}</pre>
      </div>
    </Modal>
  )
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
