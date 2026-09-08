import { useState } from "react"
import { complaint } from "@/api/auth"
import type { GeoKind, GeoSourceDraft } from "@/api/geo"
import { Modal } from "@/components/Modal"
import { field, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

export function GeoForm({
  title,
  start,
  pending,
  error,
  onSave,
  onClose,
}: {
  title: string
  start: GeoSourceDraft
  pending: boolean
  error: unknown
  onSave: (draft: GeoSourceDraft) => void
  onClose: () => void
}) {
  const t = useText()
  const [draft, setDraft] = useState(start)

  function put(change: Partial<GeoSourceDraft>) {
    setDraft({ ...draft, ...change })
  }

  return (
    <Modal
      title={title}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("geo.cancel")}
          </button>
          <button
            type="button"
            onClick={() => onSave(draft)}
            disabled={pending || draft.name.length === 0 || draft.url.length === 0}
            className={primary}
          >
            {pending ? t("geo.busy") : t("geo.save")}
          </button>
        </>
      }
    >
      <div>
        <label className={label} htmlFor="geo-name">
          {t("geo.name")}
        </label>
        <input
          id="geo-name"
          value={draft.name}
          onChange={(e) => put({ name: e.target.value })}
          className={`mt-1 ${field}`}
        />
      </div>

      <div>
        <label className={label} htmlFor="geo-kind">
          {t("geo.kind")}
        </label>
        <select
          id="geo-kind"
          value={draft.kind}
          onChange={(e) => put({ kind: e.target.value as GeoKind })}
          className={`mt-1 ${field}`}
        >
          <option value="geoip">{t("geo.kindIp")}</option>
          <option value="geosite">{t("geo.kindSite")}</option>
        </select>
      </div>

      <div>
        <label className={label} htmlFor="geo-url">
          {t("geo.url")}
        </label>
        <input
          id="geo-url"
          value={draft.url}
          onChange={(e) => put({ url: e.target.value })}
          className={`mt-1 ${field}`}
        />
      </div>

      <label className="flex items-center gap-2 text-sm text-muted" htmlFor="geo-enabled">
        <input
          id="geo-enabled"
          type="checkbox"
          checked={draft.isEnabled}
          onChange={(e) => put({ isEnabled: e.target.checked })}
          className="size-4 accent-brand"
        />
        {t("geo.enabled")}
      </label>

      {error !== null && error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(error) as TextKey)}</div>
      )}
    </Modal>
  )
}
