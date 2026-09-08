import { useState } from "react"
import { complaint } from "@/api/auth"
import type { ClientDraft } from "@/api/clients"
import { Modal } from "@/components/Modal"
import { Flag, Line } from "@/components/fields"
import { primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

export function ClientForm({
  title,
  start,
  pending,
  error,
  onSave,
  onClose,
}: {
  title: string
  start: ClientDraft
  pending: boolean
  error: unknown
  onSave: (draft: ClientDraft) => void
  onClose: () => void
}) {
  const t = useText()
  const [draft, setDraft] = useState(start)

  function put(change: Partial<ClientDraft>) {
    setDraft({ ...draft, ...change })
  }

  return (
    <Modal
      title={title}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("clients.cancel")}
          </button>
          <button
            type="button"
            onClick={() => onSave(draft)}
            disabled={pending || draft.name.length === 0 || draft.address.length === 0}
            className={primary}
          >
            {pending ? t("clients.busy") : t("clients.save")}
          </button>
        </>
      }
    >
      <div className="grid grid-cols-2 gap-3">
        <Line id="client-name" caption={t("clients.name")} value={draft.name} onChange={(name) => put({ name })} />

        <Line
          id="client-preshared"
          caption={t("clients.preshared")}
          value={draft.presharedKey}
          onChange={(presharedKey) => put({ presharedKey })}
        />

        <Line
          id="client-address"
          caption={t("clients.address")}
          value={draft.address.join(", ")}
          onChange={(text) => put({ address: parts(text) })}
          wide
        />

        <Line id="client-note" caption={t("clients.note")} value={draft.note} onChange={(note) => put({ note })} wide />

        <div className="col-span-2">
          <Flag
            id="client-enabled"
            caption={t("clients.enabled")}
            value={draft.isEnabled}
            onChange={(isEnabled) => put({ isEnabled })}
          />
        </div>
      </div>

      {error !== null && error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(error) as TextKey)}</div>
      )}
    </Modal>
  )
}

function parts(text: string): string[] {
  return text
    .split(/[\s,]+/)
    .map((one) => one.trim())
    .filter((one) => one.length > 0)
}
