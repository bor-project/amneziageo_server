import { useState } from "react"
import { complaint } from "@/api/auth"
import { useProxyAddresses } from "@/api/proxies"
import type { ProxyKind } from "@/api/proxies"
import type { ProxyTemplateDraft } from "@/api/proxyTemplates"
import { Count, Flag, Line, Multi, Part, Pick } from "@/components/fields"
import { card, danger, label, note, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

export function ProxyTemplateForm({
  start,
  pending,
  error,
  onSave,
  onClose,
  onRemove,
}: {
  start: ProxyTemplateDraft
  pending: boolean
  error: unknown
  onSave: (draft: ProxyTemplateDraft) => void
  onClose: () => void
  onRemove?: () => void
}) {
  const t = useText()
  const addresses = useProxyAddresses().data ?? []
  const [draft, setDraft] = useState(start)
  const port = draft.port > 0 && draft.port <= 65535 ? "" : t("error.badPort")
  const ready = !pending && draft.name.trim().length > 0 && port.length === 0

  function put(change: Partial<ProxyTemplateDraft>) {
    setDraft({ ...draft, ...change })
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("templates.partMain")}>
        <Line
          id="proxy-template-name"
          caption={t("templates.name")}
          value={draft.name}
          onChange={(name) => put({ name })}
          wide
        />
        <Pick
          id="proxy-template-kind"
          caption={t("proxies.kind")}
          value={draft.kind}
          onChange={(value) => put({ kind: value as ProxyKind })}
          hint={t("proxies.kindHint")}
        >
          <option value="ws">{t("proxies.kindWs")}</option>
          <option value="wg">{t("proxies.kindWg")}</option>
        </Pick>
        <Count
          id="proxy-template-port"
          caption={t("templates.startPort")}
          value={draft.port}
          onChange={(value) => put({ port: value })}
        />
        {port.length > 0 && <div className="text-xs text-alarm sm:col-span-2">{port}</div>}
      </Part>

      <Part title={t("proxies.serving")}>
        <Flag
          id="proxy-template-opened"
          caption={t("proxies.opened")}
          value={draft.opened}
          onChange={(opened) => put({ opened })}
        />
        {draft.kind === "ws" && (
          <Flag
            id="proxy-template-path"
            caption={t("proxies.makePath")}
            value={draft.makePath}
            onChange={(makePath) => put({ makePath })}
          />
        )}
        {draft.kind === "wg" && (
          <Line
            id="proxy-template-target"
            caption={t("proxies.target")}
            value={draft.target}
            onChange={(target) => put({ target })}
            hint={t("proxies.targetHint")}
            wide
          />
        )}
        <div className="sm:col-span-2">
          <label className={label} htmlFor="proxy-template-sources">
            {t("proxies.sources")}
          </label>
          <div className="mt-1">
            <Multi
              id="proxy-template-sources"
              value={draft.sources}
              offers={addresses}
              placeholder={t("proxies.anySource")}
              onChange={(sources) => put({ sources })}
            />
          </div>
          <div className={note}>{t("proxies.sourcesHint")}</div>
        </div>
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
