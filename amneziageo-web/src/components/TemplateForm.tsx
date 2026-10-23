import { useState } from "react"
import { complaint } from "@/api/auth"
import { useClients } from "@/api/clients"
import { useDnsCheck, useResolver } from "@/api/dns"
import { usePresets } from "@/api/presets"
import { useTemplateDefaults, useTemplatePreview, useTemplates } from "@/api/templates"
import type { Template, TemplateDraft, TemplatePreview } from "@/api/templates"
import { DnsOutcome, DnsPick, DnsServers } from "@/components/DnsField"
import { choiceKey, lacksServers } from "@/components/dnsWords"
import { EntryList } from "@/components/EntryList"
import { Hinted, Line, Links, Part, Switch } from "@/components/fields"
import { clientItems, presetItems } from "@/components/members"
import { ResolverTrouble } from "@/components/ResolverTrouble"
import { troubleOf } from "@/components/trouble"
import { danger, field, footer, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text } from "@/i18n"
import { same } from "@/store/draftSlice"

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
  const presets = usePresets().data ?? []
  const templates = useTemplates().data ?? []
  const titles = new Map(templates.map((one): [number, string] => [one.id, one.name]))
  const members = clientItems(useClients().data ?? [], (one) =>
    one.templateId === null ? t("clients.noTemplate") : (titles.get(one.templateId) ?? ""),
  )
  const resolver = useResolver(false).data
  const [draft, setDraft] = useState(start)
  const [touched, setTouched] = useState(false)
  const preview = useTemplatePreview(touched ? draft.entries : [], draft.standIns)
  const found = preview.data ?? kept(held)
  const total = found === undefined || preview.isFetching ? null : found.total
  const heavy = total !== null && total > stopAt
  const edited = !same(draft, start)
  const check = useDnsCheck({ id: held?.id, template: draft })
  const lacking = lacksServers(draft.dnsMode, draft.dns, draft.standIns)

  function put(change: Partial<TemplateDraft>) {
    setDraft({ ...draft, ...change })
  }

  function list(entries: string[]) {
    setTouched(true)
    put({ entries })
  }

  function stand(standIns: boolean) {
    setTouched(true)
    put({ standIns })
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

      <Part title={t("templates.partClients")}>
        <div className="sm:col-span-2">
          <Links
            id="template-clients"
            value={draft.clients.map(String)}
            items={members}
            placeholder={t("templates.addClient")}
            onChange={(keys) => put({ clients: keys.map(Number) })}
          />
        </div>
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
              standIns={draft.standIns}
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

        <div className="sm:col-span-2">
          <Switch
            id="template-stand-ins"
            caption={<Hinted caption={t("templates.standIns")} text={t("templates.standInsHint")} />}
            value={draft.standIns}
            onChange={stand}
          />
          {draft.standIns && <ResolverTrouble trouble={troubleOf(resolver)} kept="templates.troubleKept" />}
        </div>

        <div className="sm:col-span-2">
          <label className={label} htmlFor="template-presets">
            <Hinted caption={t("templates.presets")} text={t("templates.presetsHint")} />
          </label>
          <div className="mt-1">
            <Links
              id="template-presets"
              value={draft.presets.map(String)}
              items={presetItems(presets)}
              placeholder={t(presets.length === 0 ? "templates.noPresets" : "templates.pickPresets")}
              onChange={(keys) => put({ presets: keys.map(Number), presetsOnly: draft.presetsOnly && keys.length > 0 })}
            />
          </div>
        </div>

        <div className="sm:col-span-2">
          <Switch
            id="template-presets-only"
            caption={<Hinted caption={t("templates.presetsOnly")} text={t("templates.presetsOnlyHint")} />}
            value={draft.presetsOnly}
            onChange={(presetsOnly) => put({ presetsOnly })}
            disabled={draft.presets.length === 0}
            title={draft.presets.length === 0 ? t("templates.presetsOnlyNeedsPreset") : undefined}
          />
        </div>
      </Part>

      <Part title={t("templates.partNetwork")}>
        <DnsPick
          id="template-dns-mode"
          level="template"
          mode={draft.dnsMode}
          above={t("dnsPick.asServer", { choice: t(choiceKey(resolver?.filesMode ?? "routing")) })}
          onChange={(dnsMode) => put({ dnsMode })}
          locked={draft.standIns}
        />
        {!draft.standIns && draft.dnsMode === "own" ? (
          <DnsServers id="template-dns" mode={draft.dnsMode} servers={draft.dns} onChange={(dns) => put({ dns })} />
        ) : (
          <div className="hidden sm:block" />
        )}
        <DnsOutcome check={check.data} caption="dnsCheck.lineTemplate" view="template" skip={["standins-off", "outside"]} />

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

      <div className={footer}>
        {onRemove !== undefined && (
          <button type="button" onClick={onRemove} className={`mr-auto ${danger}`}>
            {t("templates.remove")}
          </button>
        )}
        <button type="button" onClick={onClose} disabled={!edited || pending} className={secondary}>
          {t("templates.cancel")}
        </button>
        <button
          type="button"
          onClick={() => onSave(draft)}
          disabled={!edited || pending || preview.isFetching || heavy || lacking || draft.name.trim().length === 0}
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
