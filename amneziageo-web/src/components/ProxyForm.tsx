import { useState } from "react"
import { complaint } from "@/api/auth"
import { uncertified } from "@/api/proxies"
import type { ProxyCertificate, ProxyDraft, ProxyKind } from "@/api/proxies"
import { Count, Flag, Line, Multi, Part, Pick, Switch } from "@/components/fields"
import { card, label, note, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function ProxyForm({
  start,
  panel,
  addresses,
  pending,
  error,
  onSave,
  onClose,
}: {
  start: ProxyDraft
  panel: ProxyCertificate | undefined
  addresses: string[]
  pending: boolean
  error: unknown
  onSave: (draft: ProxyDraft) => void
  onClose: () => void
}) {
  const t = useText()
  const [draft, setDraft] = useState<ProxyDraft>(start)
  const bare = uncertified(panel, draft)
  const fault = error !== null && error !== undefined ? complaint(error) : null

  function put(part: Partial<ProxyDraft>) {
    setDraft({ ...draft, ...part })
  }

  function take(kind: ProxyKind) {
    put(
      kind === "wg"
        ? { kind, path: "", certificate: "", certificateKey: "", target: draft.target || start.target }
        : { kind, target: "", path: draft.path || start.path },
    )
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
        />
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
        <Count
          id="proxy-port"
          caption={t("proxies.port")}
          value={draft.port}
          onChange={(v) => put({ port: v })}
          hint={t("proxies.portHint")}
        />
        <Flag
          id="proxy-opened"
          caption={t("proxies.opened")}
          value={draft.opened}
          onChange={(v) => put({ opened: v })}
        />
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
        <button type="button" onClick={onClose} className={secondary}>
          {t("proxies.cancel")}
        </button>
        <button type="button" onClick={() => onSave(draft)} disabled={pending} className={primary}>
          {t("proxies.save")}
        </button>
      </div>
    </div>
  )
}
