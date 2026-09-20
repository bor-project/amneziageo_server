import { useState } from "react"
import { complaint } from "@/api/auth"
import { useTemplateDefaults, useTemplatePreview } from "@/api/templates"
import type { Template, TemplateDraft, TemplatePreview } from "@/api/templates"
import { EntryList } from "@/components/EntryList"
import { Line, Part } from "@/components/fields"
import { card, danger, field, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text } from "@/i18n"

const slowAt = 12000
const stopAt = 16000

export function TemplateForm({
  start,
  held,
  pending,
  error,
  onSave,
  onClose,
  onRemove,
}: {
  start: TemplateDraft
  held?: Template
  pending: boolean
  error: unknown
  onSave: (draft: TemplateDraft) => void
  onClose: () => void
  onRemove?: () => void
}) {
  const t = useText()
  const defaults = useTemplateDefaults().data
  const [draft, setDraft] = useState(start)
  const [servers, setServers] = useState(start.dns.join(", "))
  const [touched, setTouched] = useState(false)
  const preview = useTemplatePreview(touched ? draft.entries : [])
  const found = preview.data ?? kept(held)
  const total = found === undefined || preview.isFetching ? null : found.total
  const heavy = total !== null && total > stopAt

  function put(change: Partial<TemplateDraft>) {
    setDraft({ ...draft, ...change })
  }

  function list(entries: string[]) {
    setTouched(true)
    put({ entries })
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("templates.partMain")}>
        <Line
          id="template-name"
          caption={t("templates.name")}
          value={draft.name}
          onChange={(name) => put({ name })}
          wide
        />
      </Part>

      <Part title={t("templates.partRouting")}>
        <div className="sm:col-span-2">
          <div className="flex items-baseline justify-between gap-3">
            <label className={label} htmlFor="template-entries">
              {t("templates.allowed")}
            </label>
            {draft.entries.length > 0 && (
              <div className="flex items-baseline gap-2 text-xs">
                <span className="text-muted">{count(t, total)}</span>
                {total !== null && total > slowAt && (
                  <span className={heavy ? "text-alarm" : "text-warn"}>
                    {t(heavy ? "templates.addressesTooMany" : "templates.addressesSlow")}
                  </span>
                )}
              </div>
            )}
          </div>
          <div className="mt-1">
            <EntryList
              id="template-entries"
              value={draft.entries}
              parts={found?.parts}
              placeholder={draft.entries.length === 0 ? (defaults?.allowedIps.join(", ") ?? "") : ""}
              onChange={list}
            />
          </div>
        </div>

        {draft.entries.length > 0 && found !== undefined && found.missed.length > 0 && (
          <div className="text-xs text-warn sm:col-span-2">
            {t("templates.missed", { list: found.missed.join(", ") })}
          </div>
        )}

        {preview.error !== null && (
          <div className="text-xs text-alarm sm:col-span-2">{t(complaint(preview.error))}</div>
        )}
      </Part>

      <Part title={t("templates.partNetwork")}>
        <Line
          id="template-dns"
          caption={t("templates.dns")}
          value={servers}
          placeholder={defaults?.dns.join(", ") ?? ""}
          onChange={setServers}
          wide
        />

        <Maybe
          id="template-mtu"
          caption={t("templates.mtu")}
          value={draft.mtu}
          placeholder={defaults === undefined ? "" : String(defaults.mtu)}
          onChange={(mtu) => put({ mtu })}
        />

        <Maybe
          id="template-keepalive"
          caption={t("templates.keepalive")}
          value={draft.keepalive}
          placeholder={defaults === undefined ? "" : String(defaults.keepalive)}
          onChange={(keepalive) => put({ keepalive })}
        />
      </Part>

      {error !== null && error !== undefined && <div className="text-sm text-alarm">{t(complaint(error))}</div>}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        {onRemove !== undefined && (
          <button type="button" onClick={onRemove} className={`mr-auto ${danger}`}>
            {t("templates.remove")}
          </button>
        )}
        <button type="button" onClick={onClose} className={secondary}>
          {t("templates.cancel")}
        </button>
        <button
          type="button"
          onClick={() => onSave({ ...draft, dns: parts(servers) })}
          disabled={pending || preview.isFetching || heavy || draft.name.trim().length === 0}
          className={primary}
        >
          {pending ? t("templates.busy") : t("templates.save")}
        </button>
      </div>
    </div>
  )
}

function Maybe({
  id,
  caption,
  value,
  placeholder,
  onChange,
}: {
  id: string
  caption: string
  value: number | null
  placeholder: string
  onChange: (value: number | null) => void
}) {
  return (
    <div>
      <label className={label} htmlFor={id}>
        {caption}
      </label>
      <input
        id={id}
        type="number"
        value={value ?? ""}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
        className={`mt-1 ${field}`}
      />
    </div>
  )
}

function kept(held: Template | undefined): TemplatePreview | undefined {
  return held === undefined
    ? undefined
    : { total: held.allowedIps.length, allowedIps: held.allowedIps, missed: held.missed, parts: [] }
}

function count(t: Text, total: number | null): string {
  return total === null ? t("templates.refreshing") : t("templates.addressesCount", { count: String(total) })
}

function parts(text: string): string[] {
  return text
    .split(/[\s,]+/)
    .map((one) => one.trim())
    .filter((one) => one.length > 0)
}
