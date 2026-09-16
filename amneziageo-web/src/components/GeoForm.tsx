import { useState } from "react"
import { complaint } from "@/api/auth"
import type { GeoKind, GeoSourceDraft } from "@/api/geo"
import { Flag, Line, Part } from "@/components/fields"
import { card, field, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

export function GeoForm({
  start,
  pending,
  error,
  onSave,
  onClose,
}: {
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
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("geo.partMain")}>
        <Line id="geo-name" caption={t("geo.name")} value={draft.name} onChange={(name) => put({ name })} />

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

        <Line id="geo-url" caption={t("geo.url")} value={draft.url} onChange={(url) => put({ url })} wide />

        <div className="sm:col-span-2">
          <Flag
            id="geo-enabled"
            caption={t("geo.enabled")}
            value={draft.isEnabled}
            onChange={(isEnabled) => put({ isEnabled })}
          />
        </div>
      </Part>

      {error !== null && error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(error) as TextKey)}</div>
      )}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
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
      </div>
    </div>
  )
}
