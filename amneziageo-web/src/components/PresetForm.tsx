import { useState } from "react"
import { complaint } from "@/api/auth"
import type { PresetDraft } from "@/api/presets"
import { EntryList } from "@/components/EntryList"
import { Flag, Hinted, Line, Part } from "@/components/fields"
import { danger, footer, label, primary, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { same } from "@/store/draftSlice"

type Bucket = "proxy" | "direct" | "block"

const badge = "rounded-md bg-active px-2 py-0.5 text-xs font-normal text-muted"

const buckets: { key: Bucket; caption: TextKey; hint: TextKey }[] = [
  { key: "proxy", caption: "presets.proxy", hint: "presets.proxyHint" },
  { key: "direct", caption: "presets.direct", hint: "presets.directHint" },
  { key: "block", caption: "presets.block", hint: "presets.blockHint" },
]

export function PresetForm({
  start,
  held,
  pending,
  error,
  onSave,
  onClose,
  onRemove,
}: {
  start: PresetDraft
  held?: { uid: string; updatedUtc: string }
  pending: boolean
  error: unknown
  onSave: (draft: PresetDraft) => void
  onClose: () => void
  onRemove?: () => void
}) {
  const t = useText()
  const language = useLanguage()
  const [draft, setDraft] = useState(start)
  const edited = !same(draft, start)

  function put(change: Partial<PresetDraft>) {
    setDraft({ ...draft, ...change })
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part
        title={
          <span className="flex flex-wrap items-center gap-x-2.5 gap-y-1.5">
            {t("presets.partMain")}
            {held !== undefined && (
              <>
                <span className={`font-mono ${badge}`}>{held.uid}</span>
                <span className={badge}>
                  {t("presets.updated", { time: new Date(held.updatedUtc).toLocaleString(language) })}
                </span>
              </>
            )}
          </span>
        }
      >
        <Line
          id="preset-name"
          caption={t("presets.name")}
          value={draft.name}
          onChange={(name) => put({ name })}
          wide
        />
        <div className="sm:col-span-2">
          <Flag
            id="preset-default"
            caption={<Hinted caption={t("presets.isDefault")} text={t("presets.isDefaultHint")} />}
            value={draft.isDefault}
            onChange={(isDefault) => put({ isDefault })}
          />
        </div>
        <div className="text-xs text-muted sm:col-span-2">{t("presets.about")}</div>
      </Part>

      <Part title={t("presets.partLists")}>
        {buckets.map((bucket) => (
          <div key={bucket.key} className="sm:col-span-2">
            <label className={label} htmlFor={`preset-${bucket.key}`}>
              <Hinted caption={t(bucket.caption)} text={t(bucket.hint)} />
            </label>
            <div className="mt-1">
              <EntryList
                id={`preset-${bucket.key}`}
                value={draft[bucket.key]}
                parts={undefined}
                placeholder=""
                onChange={(entries) => put({ [bucket.key]: entries })}
              />
            </div>
          </div>
        ))}

        <div className="sm:col-span-2">
          <Flag
            id="preset-full"
            caption={<Hinted caption={t("presets.full")} text={t("presets.fullHint")} />}
            value={draft.full}
            onChange={(full) => put({ full })}
          />
        </div>

        <div className="sm:col-span-2">
          <Flag
            id="preset-udp"
            caption={<Hinted caption={t("presets.allUdp")} text={t("presets.allUdpHint")} />}
            value={draft.allUdp}
            onChange={(allUdp) => put({ allUdp })}
          />
        </div>
      </Part>

      {error !== null && error !== undefined && <div className="text-sm text-alarm">{t(complaint(error))}</div>}

      <div className={footer}>
        {onRemove !== undefined && (
          <button type="button" onClick={onRemove} className={`mr-auto ${danger}`}>
            {t("presets.remove")}
          </button>
        )}
        <button type="button" onClick={onClose} disabled={!edited || pending} className={secondary}>
          {t("presets.cancel")}
        </button>
        <button
          type="button"
          onClick={() => onSave(draft)}
          disabled={!edited || pending || draft.name.trim().length === 0}
          className={primary}
        >
          {pending ? t("presets.busy") : t("presets.save")}
        </button>
      </div>
    </div>
  )
}
