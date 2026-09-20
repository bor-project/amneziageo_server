import { useState } from "react"
import { complaint } from "@/api/auth"
import { uncertified, useProxies } from "@/api/proxies"
import type { ProxyCertificate, ProxyDraft, ProxyKind } from "@/api/proxies"
import { useProxyTemplates } from "@/api/proxyTemplates"
import type { ProxyTemplate } from "@/api/proxyTemplates"
import { Count, Flag, Folded, Line, Multi, Part, Pick, Switch } from "@/components/fields"
import { portFault, usePortHolders } from "@/components/ports"
import { card, danger, field, label, note, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text } from "@/i18n"

export function ProxyForm({
  start,
  panel,
  addresses,
  self,
  pending,
  error,
  onSave,
  onClose,
  onRemove,
}: {
  start: ProxyDraft
  panel: ProxyCertificate | undefined
  addresses: string[]
  self?: number
  pending: boolean
  error: unknown
  onSave: (draft: ProxyDraft) => void
  onClose: () => void
  onRemove?: () => void
}) {
  const t = useText()
  const templates = useProxyTemplates().data ?? []
  const others = (useProxies().data ?? []).filter((one) => one.id !== self)
  const held = usePortHolders({ proxy: self })
  const [draft, setDraft] = useState<ProxyDraft>(start)
  const chosen = templates.find((one) => one.id === draft.templateId)
  const bare = uncertified(panel, draft)
  const port = portFault(t, draft.port, held)
  const name = others.some((one) => one.name === draft.name.trim()) ? t("error.nameTaken") : ""
  const fault = error !== null && error !== undefined ? complaint(error) : null
  const ready = !pending && draft.name.trim().length > 0 && port.length === 0 && name.length === 0

  function put(part: Partial<ProxyDraft>) {
    setDraft({ ...draft, ...part })
  }

  function take(kind: ProxyKind) {
    put(kind === "wg" ? { kind, path: "", certificate: "", certificateKey: "" } : { kind, target: "" })
  }

  function choose(value: string) {
    const templateId = value === "" ? null : Number(value)
    const found = templates.find((one) => one.id === templateId)

    if (found === undefined) {
      put({ templateId })

      return
    }

    put({
      templateId,
      kind: found.kind,
      opened: found.opened,
      target: found.target,
      sources: found.sources,
      port: self === undefined ? found.port : draft.port,
      path: found.kind === "wg" ? "" : draft.path,
    })
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("proxies.serving")}>
        <div className="sm:col-span-2">
          <Switch
            id="proxy-on"
            caption={t("proxies.enabled")}
            value={draft.isEnabled}
            onChange={(v) => put({ isEnabled: v })}
          />
        </div>
        <Line
          id="proxy-name"
          caption={t("proxies.name")}
          value={draft.name}
          onChange={(v) => put({ name: v })}
          hint={t("proxies.nameHint")}
          fault={name}
        />
        <Count
          id="proxy-port"
          caption={t("proxies.port")}
          value={draft.port}
          onChange={(v) => put({ port: v })}
          hint={t("proxies.portHint")}
        />
        {port.length > 0 && <div className="-mt-2 text-xs text-alarm">{port}</div>}
        {draft.kind === "ws" && (
          <Line
            id="proxy-path"
            caption={t("proxies.path")}
            value={draft.path}
            onChange={(v) => put({ path: v })}
            hint={t("proxies.pathHint")}
            wide
          />
        )}
      </Part>

      <Part title={t("proxies.template")}>
        <Pick
          id="proxy-template"
          caption={t("proxies.template")}
          value={draft.templateId === null ? "" : String(draft.templateId)}
          onChange={choose}
          wide
        >
          <option value="">{t("proxies.noTemplate")}</option>
          {templates.map((one) => (
            <option key={one.id} value={one.id}>
              {one.name}
            </option>
          ))}
        </Pick>

        {chosen !== undefined && (
          <Folded caption={t("templates.values")}>
            <Inherited t={t} template={chosen} />
          </Folded>
        )}

        {draft.templateId === null && (
          <>
            <Pick
              id="proxy-kind"
              caption={t("proxies.kind")}
              value={draft.kind}
              onChange={(v) => take(v as ProxyKind)}
              hint={t("proxies.kindHint")}
            >
              <option value="ws">{t("proxies.kindWs")}</option>
              <option value="wg">{t("proxies.kindWg")}</option>
            </Pick>
            <Flag
              id="proxy-opened"
              caption={t("proxies.opened")}
              value={draft.opened}
              onChange={(v) => put({ opened: v })}
            />
            {draft.kind === "wg" && (
              <Line
                id="proxy-target"
                caption={t("proxies.target")}
                value={draft.target}
                onChange={(v) => put({ target: v })}
                hint={t("proxies.targetHint")}
                wide
              />
            )}
            <div className="sm:col-span-2">
              <label className={label} htmlFor="proxy-sources">
                {t("proxies.sources")}
              </label>
              <div className="mt-1">
                <Multi
                  id="proxy-sources"
                  value={draft.sources}
                  offers={addresses}
                  placeholder={t("proxies.anySource")}
                  onChange={(sources) => put({ sources })}
                />
              </div>
              <div className={note}>{t("proxies.sourcesHint")}</div>
            </div>
          </>
        )}
      </Part>

      {draft.kind === "ws" && (
        <Part title={t("proxies.tls")}>
          <Line
            id="proxy-certificate"
            caption={t("proxies.certificate")}
            value={draft.certificate}
            onChange={(v) => put({ certificate: v })}
            placeholder={panel?.chain ?? ""}
            hint={t("proxies.certificateHint")}
            wide
          />
          <Line
            id="proxy-certificate-key"
            caption={t("proxies.certificateKey")}
            value={draft.certificateKey}
            onChange={(v) => put({ certificateKey: v })}
            placeholder={panel?.key ?? ""}
            hint={t("proxies.certificateKeyHint")}
            wide
          />
          {bare && <div className="text-sm text-alarm sm:col-span-2">{t("error.noCertificate")}</div>}
        </Part>
      )}

      {fault !== null && !(bare && fault === "error.noCertificate") && (
        <div className="text-sm text-alarm">{t(fault)}</div>
      )}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        {onRemove !== undefined && (
          <button type="button" onClick={onRemove} className={`mr-auto ${danger}`}>
            {t("proxies.remove")}
          </button>
        )}
        <button type="button" onClick={onClose} className={secondary}>
          {t("proxies.cancel")}
        </button>
        <button
          type="button"
          onClick={() => onSave({ ...draft, name: draft.name.trim() })}
          disabled={!ready}
          className={primary}
        >
          {t("proxies.save")}
        </button>
      </div>
    </div>
  )
}

function Inherited({ t, template }: { t: Text; template: ProxyTemplate }) {
  return (
    <>
      <Fixed
        caption={t("proxies.kind")}
        value={template.kind === "wg" ? t("proxies.kindWg") : t("proxies.kindWs")}
      />
      <Fixed caption={t("proxies.opened")} value={template.opened ? t("action.yes") : t("action.no")} />
      {template.kind === "wg" && <Fixed caption={t("proxies.target")} value={template.target} />}
      <Fixed
        caption={t("proxies.sources")}
        value={template.sources.length === 0 ? t("proxies.anySource") : template.sources.join(", ")}
      />
    </>
  )
}

function Fixed({ caption, value }: { caption: string; value: string }) {
  return (
    <div>
      <span className={label}>{caption}</span>
      <div className={`mt-1 truncate ${field}`} title={value}>
        {value}
      </div>
    </div>
  )
}
