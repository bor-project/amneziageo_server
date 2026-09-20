import { useState } from "react"
import { span } from "@/address"
import { complaint } from "@/api/auth"
import type { Obfuscation } from "@/api/configs"
import type { InterfaceTemplateDraft } from "@/api/interfaceTemplates"
import { useTemplates } from "@/api/templates"
import { ObfuscationFields } from "@/components/Obfuscation"
import { Count, Line, Part, Pick } from "@/components/fields"
import { card, danger, primary, secondary } from "@/components/styles"
import { parts } from "@/format"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

export function InterfaceTemplateForm({
  start,
  pending,
  error,
  onSave,
  onClose,
  onRemove,
  held = false,
}: {
  start: InterfaceTemplateDraft
  pending: boolean
  error: unknown
  onSave: (draft: InterfaceTemplateDraft) => void
  onClose: () => void
  onRemove?: () => void
  held?: boolean
}) {
  const t = useText()
  const clients = useTemplates().data ?? []
  const [draft, setDraft] = useState(start)
  const subnet = span(draft.subnet) === null ? t("error.badAddress") : ""
  const port = draft.listenPort > 0 && draft.listenPort <= 65535 ? "" : t("error.badPort")
  const ready = !pending && draft.name.trim().length > 0 && subnet.length === 0 && port.length === 0
  const twisted = JSON.stringify(draft.obfuscation) !== JSON.stringify(start.obfuscation)

  function put(change: Partial<InterfaceTemplateDraft>) {
    setDraft({ ...draft, ...change })
  }

  function twist(change: Partial<Obfuscation>) {
    setDraft({ ...draft, obfuscation: { ...draft.obfuscation, ...change } })
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("templates.partMain")}>
        <Line
          id="interface-template-name"
          caption={t("templates.name")}
          value={draft.name}
          onChange={(name) => put({ name })}
          wide
        />

        <Pick
          id="interface-template-client"
          caption={t("templates.clientTemplate")}
          value={draft.clientTemplateId === null ? "" : String(draft.clientTemplateId)}
          onChange={(value) => put({ clientTemplateId: value === "" ? null : Number(value) })}
          wide
        >
          <option value="">{t("templates.defaultClient")}</option>
          {clients.map((one) => (
            <option key={one.id} value={one.id}>
              {one.name}
            </option>
          ))}
        </Pick>
      </Part>

      <Part title={t("configs.network")}>
        <Count
          id="interface-template-port"
          caption={t("templates.startPort")}
          value={draft.listenPort}
          onChange={(listenPort) => put({ listenPort })}
        />
        <Line
          id="interface-template-subnet"
          caption={t("templates.startSubnet")}
          value={draft.subnet}
          onChange={(subnetValue) => put({ subnet: subnetValue.trim() })}
          fault={subnet}
        />
        {port.length > 0 && <div className="text-xs text-alarm sm:col-span-2">{port}</div>}
        <Line
          id="interface-template-blocked"
          caption={t("configs.blocked")}
          value={draft.blocked.join(", ")}
          onChange={(value) => put({ blocked: parts(value) })}
          wide
        />
      </Part>

      <Part title={t("configs.clients")}>
        <Line
          id="interface-template-allowed"
          caption={t("configs.allowed")}
          value={draft.allowedIps.join(", ")}
          onChange={(value) => put({ allowedIps: parts(value) })}
          wide
        />
        <Line
          id="interface-template-dns"
          caption={t("configs.dns")}
          value={draft.dns.join(", ")}
          onChange={(value) => put({ dns: parts(value) })}
        />
        <Count
          id="interface-template-mtu"
          caption={t("configs.mtu")}
          value={draft.mtu}
          onChange={(mtu) => put({ mtu })}
        />
        <Count
          id="interface-template-keepalive"
          caption={t("configs.keepalive")}
          value={draft.keepalive}
          onChange={(keepalive) => put({ keepalive })}
        />
        <Count
          id="interface-template-online"
          caption={t("configs.offlineAfter")}
          value={draft.offlineAfter}
          onChange={(offlineAfter) => put({ offlineAfter })}
          hint={t("configs.offlineAfterHint")}
        />
      </Part>

      <Part title={t("configs.obfuscation")}>
        <ObfuscationFields id="interface-template" cover={draft.obfuscation} onChange={twist} />
        {held && twisted && (
          <div className="text-xs text-alarm sm:col-span-2">{t("templates.obfuscationWarning")}</div>
        )}
      </Part>

      {error !== null && error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(error) as TextKey)}</div>
      )}

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
          onClick={() => onSave({ ...draft, name: draft.name.trim() })}
          disabled={!ready}
          className={primary}
        >
          {pending ? t("templates.busy") : t("templates.save")}
        </button>
      </div>
    </div>
  )
}
