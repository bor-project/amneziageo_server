import { useState } from "react"
import { complaint } from "@/api/auth"
import { useTemplateDefaults } from "@/api/templates"
import type { Template, TemplateDraft } from "@/api/templates"
import { EntryList } from "@/components/EntryList"
import { Modal } from "@/components/Modal"
import { TextBlock } from "@/components/TextBlock"
import { Line } from "@/components/fields"
import { field, label, primary, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"

export function TemplateForm({
  title,
  start,
  held,
  pending,
  error,
  onSave,
  onClose,
}: {
  title: string
  start: TemplateDraft
  held?: Template
  pending: boolean
  error: unknown
  onSave: (draft: TemplateDraft) => void
  onClose: () => void
}) {
  const t = useText()
  const language = useLanguage()
  const defaults = useTemplateDefaults().data
  const [draft, setDraft] = useState(start)
  const [servers, setServers] = useState(start.dns.join(", "))

  function put(change: Partial<TemplateDraft>) {
    setDraft({ ...draft, ...change })
  }

  return (
    <Modal
      title={title}
      wide
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("templates.cancel")}
          </button>
          <button
            type="button"
            onClick={() => onSave({ ...draft, dns: parts(servers) })}
            disabled={pending || draft.name.trim().length === 0}
            className={primary}
          >
            {pending ? t("templates.busy") : t("templates.save")}
          </button>
        </>
      }
    >
      <div className="grid grid-cols-2 gap-3">
        <Line
          id="template-name"
          caption={t("templates.name")}
          value={draft.name}
          onChange={(name) => put({ name })}
          wide
        />

        <div className="col-span-2">
          <label className={label} htmlFor="template-entries">
            {t("templates.allowed")}
          </label>
          <div className="mt-1">
            <EntryList
              id="template-entries"
              value={draft.entries}
              placeholder={draft.entries.length === 0 ? (defaults?.allowedIps.join(", ") ?? "") : ""}
              onChange={(entries) => put({ entries })}
            />
          </div>
        </div>

        {held !== undefined && held.entries.length > 0 && (
          <div className="col-span-2">
            <div className={label}>{t("templates.resolved")}</div>
            <TextBlock className="mt-1 max-h-40 overflow-auto rounded border border-line bg-canvas p-3 text-xs text-ink">
              {held.allowedIps.join("\n")}
            </TextBlock>
            <div className="mt-1 text-xs text-muted">
              {t("templates.resolvedLine", {
                count: String(held.allowedIps.length),
                time: held.refreshedUtc === null ? "-" : new Date(held.refreshedUtc).toLocaleString(language),
              })}
            </div>
            {held.missed.length > 0 && (
              <div className="mt-1 text-xs text-warn">{t("templates.missed", { list: held.missed.join(", ") })}</div>
            )}
          </div>
        )}

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
      </div>

      {error !== null && error !== undefined && <div className="text-sm text-alarm">{t(complaint(error))}</div>}
    </Modal>
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

function parts(text: string): string[] {
  return text
    .split(/[\s,]+/)
    .map((one) => one.trim())
    .filter((one) => one.length > 0)
}
