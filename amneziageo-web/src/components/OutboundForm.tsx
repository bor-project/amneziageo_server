import { useState } from "react"
import { complaint } from "@/api/auth"
import type { Obfuscation } from "@/api/configs"
import { useImportOutbound, useOutboundKeys } from "@/api/outbounds"
import type { OutboundDraft, OutboundKind } from "@/api/outbounds"
import { Modal } from "@/components/Modal"
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

  const cover = draft.obfuscation
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
          <div className="col-span-2 grid grid-cols-4 gap-3">
            <Count id="outbound-jc" caption={t("configs.jc")} value={cover.jc} onChange={(value) => twist({ jc: value })} />
            <Count id="outbound-jmin" caption={t("configs.jmin")} value={cover.jmin} onChange={(value) => twist({ jmin: value })} />
            <Count id="outbound-jmax" caption={t("configs.jmax")} value={cover.jmax} onChange={(value) => twist({ jmax: value })} />
            <Count id="outbound-s1" caption={t("configs.s1")} value={cover.s1} onChange={(value) => twist({ s1: value })} />
            <Count id="outbound-s2" caption={t("configs.s2")} value={cover.s2} onChange={(value) => twist({ s2: value })} />
            <Count id="outbound-s3" caption={t("configs.s3")} value={cover.s3} onChange={(value) => twist({ s3: value })} />
            <Count id="outbound-s4" caption={t("configs.s4")} value={cover.s4} onChange={(value) => twist({ s4: value })} />
            <Line id="outbound-h1" caption={t("configs.h1")} value={cover.h1} onChange={(value) => twist({ h1: value })} />
            <Line id="outbound-h2" caption={t("configs.h2")} value={cover.h2} onChange={(value) => twist({ h2: value })} />
            <Line id="outbound-h3" caption={t("configs.h3")} value={cover.h3} onChange={(value) => twist({ h3: value })} />
            <Line id="outbound-h4" caption={t("configs.h4")} value={cover.h4} onChange={(value) => twist({ h4: value })} />
            <Line
              id="outbound-padding"
              caption={t("configs.padding")}
              value={cover.contentPaddingAddition}
              onChange={(value) => twist({ contentPaddingAddition: value })}
            />
            <Line
              id="outbound-rekey-after"
              caption={t("configs.rekeyAfter")}
              value={cover.rekeyAfterTime}
              onChange={(value) => twist({ rekeyAfterTime: value })}
            />
            <Line
              id="outbound-rekey-timeout"
              caption={t("configs.rekeyTimeout")}
              value={cover.rekeyTimeout}
              onChange={(value) => twist({ rekeyTimeout: value })}
            />
            <Line
              id="outbound-reject-after"
              caption={t("configs.rejectAfter")}
              value={cover.rejectAfterTime}
              onChange={(value) => twist({ rejectAfterTime: value })}
            />
            <Line
              id="outbound-keepalive-timeout"
              caption={t("configs.keepaliveTimeout")}
              value={cover.keepaliveTimeout}
              onChange={(value) => twist({ keepaliveTimeout: value })}
            />
            <Line
              id="outbound-attempts"
              caption={t("configs.attempts")}
              value={cover.maxHandshakeAttempts}
              onChange={(value) => twist({ maxHandshakeAttempts: value })}
            />
          </div>
          <Line id="outbound-i1" caption={t("configs.i1")} value={cover.i1 ?? ""} onChange={(value) => twist({ i1: value })} />
          <Line id="outbound-i2" caption={t("configs.i2")} value={cover.i2 ?? ""} onChange={(value) => twist({ i2: value })} />
          <Line id="outbound-i3" caption={t("configs.i3")} value={cover.i3 ?? ""} onChange={(value) => twist({ i3: value })} />
          <Line id="outbound-i4" caption={t("configs.i4")} value={cover.i4 ?? ""} onChange={(value) => twist({ i4: value })} />
          <Line id="outbound-i5" caption={t("configs.i5")} value={cover.i5 ?? ""} onChange={(value) => twist({ i5: value })} />
          <Line
            id="outbound-header-key"
            caption={t("configs.headerKey")}
            value={cover.headerProtectionKey}
            onChange={(value) => twist({ headerProtectionKey: value.trim() })}
            wide
          />
          <Flag
            id="outbound-trailers"
            caption={t("configs.trailers")}
            value={cover.randomTrailers}
            onChange={(value) => twist({ randomTrailers: value })}
          />
          <Flag
            id="outbound-cookies"
            caption={t("configs.cookies")}
            value={cover.disableCookies}
            onChange={(value) => twist({ disableCookies: value })}
          />
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
