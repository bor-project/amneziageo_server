import { useState } from "react"
import { complaint } from "@/api/auth"
import { useKeyPair, usePresharedKey } from "@/api/configs"
import type { ConfigDraft, Obfuscation } from "@/api/configs"
import { Modal } from "@/components/Modal"
import { Count, Flag, Line, Section } from "@/components/fields"
import { field, label, primary, secondary } from "@/components/styles"
import { parts } from "@/format"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

export function ConfigForm({
  title,
  start,
  publicKey,
  pending,
  error,
  onSave,
  onClose,
}: {
  title: string
  start: ConfigDraft
  publicKey: string
  pending: boolean
  error: unknown
  onSave: (draft: ConfigDraft) => void
  onClose: () => void
}) {
  const t = useText()
  const keys = useKeyPair()
  const shared = usePresharedKey()
  const [draft, setDraft] = useState(start)
  const [shown, setShown] = useState(publicKey)

  function put(change: Partial<ConfigDraft>) {
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

  async function secret() {
    const made = await shared.mutateAsync()
    put({ presharedKey: made.key })
  }

  const cover = draft.obfuscation

  return (
    <Modal
      title={title}
      onClose={onClose}
      wide
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("configs.cancel")}
          </button>
          <button
            type="button"
            onClick={() => onSave(draft)}
            disabled={pending || draft.name.length === 0}
            className={primary}
          >
            {pending ? t("configs.busy") : t("configs.save")}
          </button>
        </>
      }
    >
      <Section title={t("configs.network")}>
        <Line id="config-name" caption={t("configs.name")} value={draft.name} onChange={(value) => put({ name: value })} />
        <Line id="config-host" caption={t("configs.host")} value={draft.host} onChange={(value) => put({ host: value })} />
        <Count id="config-port" caption={t("configs.port")} value={draft.listenPort} onChange={(value) => put({ listenPort: value })} />
        <Count id="config-mtu" caption={t("configs.mtu")} value={draft.mtu} onChange={(value) => put({ mtu: value })} />
        <Line
          id="config-address"
          caption={t("configs.address")}
          value={draft.address.join(", ")}
          onChange={(value) => put({ address: parts(value) })}
          wide
        />
        <Line
          id="config-blocked"
          caption={t("configs.blocked")}
          value={draft.blocked.join(", ")}
          onChange={(value) => put({ blocked: parts(value) })}
          wide
        />
        <Flag
          id="config-enabled"
          caption={t("configs.enabled")}
          value={draft.isEnabled}
          onChange={(value) => put({ isEnabled: value })}
        />
        <Flag
          id="config-nat"
          caption={t("configs.nat")}
          value={draft.nat}
          onChange={(value) => put({ nat: value })}
        />
      </Section>

      <Section title={t("configs.clients")}>
        <Line
          id="config-allowed"
          caption={t("configs.allowed")}
          value={draft.allowedIps.join(", ")}
          onChange={(value) => put({ allowedIps: parts(value) })}
          wide
        />
        <Line
          id="config-dns"
          caption={t("configs.dns")}
          value={draft.dns.join(", ")}
          onChange={(value) => put({ dns: parts(value) })}
        />
        <Count
          id="config-keepalive"
          caption={t("configs.keepalive")}
          value={draft.keepalive}
          onChange={(value) => put({ keepalive: value })}
        />
      </Section>

      <Section title={t("configs.keys")}>
        <Line
          id="config-private"
          caption={t("configs.private")}
          value={draft.privateKey}
          onChange={(value) => put({ privateKey: value.trim() })}
          wide
        />
        <div className="col-span-2 flex items-end gap-3">
          <div className="min-w-0 flex-1">
            <span className={label}>{t("configs.public")}</span>
            <div className={`mt-1 truncate ${field}`}>{shown}</div>
          </div>
          <button type="button" onClick={() => void pair()} disabled={keys.isPending} className={secondary}>
            {t("configs.newKeys")}
          </button>
        </div>

        <div className="col-span-2 flex items-end gap-3">
          <div className="min-w-0 flex-1">
            <label className={label} htmlFor="config-preshared">
              {t("configs.preshared")}
            </label>
            <input
              id="config-preshared"
              value={draft.presharedKey}
              onChange={(e) => put({ presharedKey: e.target.value.trim() })}
              className={`mt-1 ${field}`}
            />
          </div>
          <button type="button" onClick={() => void secret()} disabled={shared.isPending} className={secondary}>
            {t("configs.generate")}
          </button>
        </div>
      </Section>

      <Section title={t("configs.obfuscation")}>
        <div className="col-span-2 grid grid-cols-4 gap-3">
          <Count id="config-jc" caption={t("configs.jc")} value={cover.jc} onChange={(value) => twist({ jc: value })} />
          <Count id="config-jmin" caption={t("configs.jmin")} value={cover.jmin} onChange={(value) => twist({ jmin: value })} />
          <Count id="config-jmax" caption={t("configs.jmax")} value={cover.jmax} onChange={(value) => twist({ jmax: value })} />
          <Count id="config-s1" caption={t("configs.s1")} value={cover.s1} onChange={(value) => twist({ s1: value })} />
          <Count id="config-s2" caption={t("configs.s2")} value={cover.s2} onChange={(value) => twist({ s2: value })} />
          <Count id="config-s3" caption={t("configs.s3")} value={cover.s3} onChange={(value) => twist({ s3: value })} />
          <Count id="config-s4" caption={t("configs.s4")} value={cover.s4} onChange={(value) => twist({ s4: value })} />
          <Line id="config-h1" caption={t("configs.h1")} value={cover.h1} onChange={(value) => twist({ h1: value })} />
          <Line id="config-h2" caption={t("configs.h2")} value={cover.h2} onChange={(value) => twist({ h2: value })} />
          <Line id="config-h3" caption={t("configs.h3")} value={cover.h3} onChange={(value) => twist({ h3: value })} />
          <Line id="config-h4" caption={t("configs.h4")} value={cover.h4} onChange={(value) => twist({ h4: value })} />
          <Line
            id="config-padding"
            caption={t("configs.padding")}
            value={cover.contentPaddingAddition}
            onChange={(value) => twist({ contentPaddingAddition: value })}
          />
          <Line
            id="config-rekey-after"
            caption={t("configs.rekeyAfter")}
            value={cover.rekeyAfterTime}
            onChange={(value) => twist({ rekeyAfterTime: value })}
          />
          <Line
            id="config-rekey-timeout"
            caption={t("configs.rekeyTimeout")}
            value={cover.rekeyTimeout}
            onChange={(value) => twist({ rekeyTimeout: value })}
          />
          <Line
            id="config-reject-after"
            caption={t("configs.rejectAfter")}
            value={cover.rejectAfterTime}
            onChange={(value) => twist({ rejectAfterTime: value })}
          />
          <Line
            id="config-keepalive-timeout"
            caption={t("configs.keepaliveTimeout")}
            value={cover.keepaliveTimeout}
            onChange={(value) => twist({ keepaliveTimeout: value })}
          />
          <Line
            id="config-attempts"
            caption={t("configs.attempts")}
            value={cover.maxHandshakeAttempts}
            onChange={(value) => twist({ maxHandshakeAttempts: value })}
          />
        </div>
        <Line id="config-i1" caption={t("configs.i1")} value={cover.i1 ?? ""} onChange={(value) => twist({ i1: value })} />
        <Line id="config-i2" caption={t("configs.i2")} value={cover.i2 ?? ""} onChange={(value) => twist({ i2: value })} />
        <Line id="config-i3" caption={t("configs.i3")} value={cover.i3 ?? ""} onChange={(value) => twist({ i3: value })} />
        <Line id="config-i4" caption={t("configs.i4")} value={cover.i4 ?? ""} onChange={(value) => twist({ i4: value })} />
        <Line id="config-i5" caption={t("configs.i5")} value={cover.i5 ?? ""} onChange={(value) => twist({ i5: value })} />
        <Line
          id="config-header-key"
          caption={t("configs.headerKey")}
          value={cover.headerProtectionKey}
          onChange={(value) => twist({ headerProtectionKey: value.trim() })}
          wide
        />
        <Flag
          id="config-trailers"
          caption={t("configs.trailers")}
          value={cover.randomTrailers}
          onChange={(value) => twist({ randomTrailers: value })}
        />
        <Flag
          id="config-cookies"
          caption={t("configs.cookies")}
          value={cover.disableCookies}
          onChange={(value) => twist({ disableCookies: value })}
        />
      </Section>

      {error !== null && error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(error) as TextKey)}</div>
      )}
    </Modal>
  )
}

