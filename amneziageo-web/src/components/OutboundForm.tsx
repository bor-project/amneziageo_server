import { useState } from "react"
import { complaint } from "@/api/auth"
import type { Obfuscation } from "@/api/configs"
import { useImportOutbound, useOutboundKeys } from "@/api/outbounds"
import type { OutboundDraft, OutboundKind } from "@/api/outbounds"
import { Modal } from "@/components/Modal"
import { ObfuscationFields } from "@/components/Obfuscation"
import { Count, Flag, Line, Section } from "@/components/fields"
import { field, label, primary, secondary } from "@/components/styles"
import { parts } from "@/format"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

export function OutboundForm({
  title,
  start,
  publicKey,
  pending,
  error,
  onSave,
  onClose,
}: {
  title: string
  start: OutboundDraft
  publicKey: string
  pending: boolean
  error: unknown
  onSave: (draft: OutboundDraft) => void
  onClose: () => void
}) {
  const t = useText()
  const keys = useOutboundKeys()
  const read = useImportOutbound()
  const [draft, setDraft] = useState(start)
  const [shown, setShown] = useState(publicKey)
  const [text, setText] = useState("")

  function put(change: Partial<OutboundDraft>) {
    setDraft({ ...draft, ...change })
  }

  function twist(change: Partial<Obfuscation>) {
    setDraft({ ...draft, obfuscation: { ...draft.obfuscation, ...change } })
  }

  async function pair() {
    const made = await keys.mutateAsync()
    setDraft({ ...draft, privateKey: made.privateKey })
    setShown(made.publicKey)
  }

  async function take() {
    const found = await read.mutateAsync({ name: draft.name, config: text })
    setDraft({
      ...draft,
      kind: found.kind,
      host: found.host,
      port: found.port,
      privateKey: found.privateKey ?? "",
      peerKey: found.peerKey,
      presharedKey: found.presharedKey ?? "",
      address: found.address,
      dns: found.dns,
      mtu: found.mtu,
      keepalive: found.keepalive,
      obfuscation: found.obfuscation,
    })
    setShown(found.publicKey)
    setText("")
  }

  const tunnel = draft.kind === "wg" || draft.kind === "ws"

  return (
    <Modal
      title={title}
      onClose={onClose}
      wide
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("outbounds.cancel")}
          </button>
          <button
            type="button"
            onClick={() => onSave(draft)}
            disabled={pending || draft.name.length === 0}
            className={primary}
          >
            {pending ? t("outbounds.busy") : t("outbounds.save")}
          </button>
        </>
      }
    >
      <Section title={t("outbounds.settings")}>
        <Line
          id="outbound-name"
          caption={t("outbounds.name")}
          value={draft.name}
          onChange={(value) => put({ name: value })}
        />
        <div>
          <label className={label} htmlFor="outbound-kind">
            {t("outbounds.kind")}
          </label>
          <select
            id="outbound-kind"
            value={draft.kind}
            onChange={(e) => put({ kind: e.target.value as OutboundKind })}
            className={`mt-1 ${field}`}
          >
            <option value="local">{t("outbounds.kindLocal")}</option>
            <option value="wg">{t("outbounds.kindWg")}</option>
            <option value="ws">{t("outbounds.kindWs")}</option>
          </select>
        </div>
        <Flag
          id="outbound-enabled"
          caption={t("outbounds.enabled")}
          value={draft.isEnabled}
          onChange={(value) => put({ isEnabled: value })}
        />
      </Section>

      <Section title={t("outbounds.probe")}>
        <Line
          id="outbound-probe"
          caption={t("outbounds.probeServer")}
          value={draft.probe}
          onChange={(value) => put({ probe: value })}
        />
        <Count
          id="outbound-probe-every"
          caption={t("outbounds.probeEvery")}
          value={draft.probeEvery}
          onChange={(value) => put({ probeEvery: value })}
        />
      </Section>

      {tunnel && (
        <Section title={t("outbounds.server")}>
          <Line
            id="outbound-host"
            caption={t("outbounds.host")}
            value={draft.host}
            onChange={(value) => put({ host: value })}
          />
          <Count
            id="outbound-port"
            caption={t("outbounds.port")}
            value={draft.port}
            onChange={(value) => put({ port: value })}
          />
          {draft.kind === "ws" && (
            <Line
              id="outbound-proxy"
              caption={t("outbounds.proxy")}
              value={draft.proxy}
              onChange={(value) => put({ proxy: value })}
              wide
            />
          )}
          <Line
            id="outbound-address"
            caption={t("outbounds.address")}
            value={draft.address.join(", ")}
            onChange={(value) => put({ address: parts(value) })}
            wide
          />
          <Line
            id="outbound-dns"
            caption={t("outbounds.dns")}
            value={draft.dns.join(", ")}
            onChange={(value) => put({ dns: parts(value) })}
          />
          <Count
            id="outbound-mtu"
            caption={t("outbounds.mtu")}
            value={draft.mtu}
            onChange={(value) => put({ mtu: value })}
          />
          <Count
            id="outbound-keepalive"
            caption={t("outbounds.keepalive")}
            value={draft.keepalive}
            onChange={(value) => put({ keepalive: value })}
          />
        </Section>
      )}

      {tunnel && (
        <Section title={t("outbounds.keys")}>
          <Line
            id="outbound-private"
            caption={t("outbounds.private")}
            value={draft.privateKey}
            onChange={(value) => put({ privateKey: value.trim() })}
            wide
          />
          <div className="col-span-2 flex items-end gap-3">
            <div className="min-w-0 flex-1">
              <span className={label}>{t("outbounds.public")}</span>
              <div className={`mt-1 truncate ${field}`}>{shown}</div>
            </div>
            <button type="button" onClick={() => void pair()} disabled={keys.isPending} className={secondary}>
              {t("outbounds.newKeys")}
            </button>
          </div>
          <Line
            id="outbound-peer"
            caption={t("outbounds.peerKey")}
            value={draft.peerKey}
            onChange={(value) => put({ peerKey: value.trim() })}
            wide
          />
          <Line
            id="outbound-preshared"
            caption={t("outbounds.preshared")}
            value={draft.presharedKey}
            onChange={(value) => put({ presharedKey: value.trim() })}
            wide
          />
        </Section>
      )}

      {tunnel && (
        <Section title={t("outbounds.obfuscation")}>
          <ObfuscationFields id="outbound" cover={draft.obfuscation} onChange={twist} />
        </Section>
      )}

      {tunnel && (
        <Section title={t("outbounds.import")}>
          <div className="col-span-2">
            <label className={label} htmlFor="outbound-config">
              {t("outbounds.paste")}
            </label>
            <textarea
              id="outbound-config"
              value={text}
              rows={5}
              onChange={(e) => setText(e.target.value)}
              className={`mt-1 font-mono text-xs ${field}`}
            />
          </div>
          <div className="col-span-2 flex justify-end">
            <button
              type="button"
              onClick={() => void take()}
              disabled={read.isPending || text.trim().length === 0}
              className={secondary}
            >
              {t("outbounds.read")}
            </button>
          </div>
          {read.error !== null && read.error !== undefined && (
            <div className="col-span-2 text-sm text-alarm">{t(complaint(read.error) as TextKey)}</div>
          )}
        </Section>
      )}

      {error !== null && error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(error) as TextKey)}</div>
      )}
    </Modal>
  )
}
