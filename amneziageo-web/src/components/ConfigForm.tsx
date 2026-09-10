import { useState } from "react"
import { complaint } from "@/api/auth"
import { useKeyPair, usePresharedKey } from "@/api/configs"
import type { ConfigDraft, Obfuscation } from "@/api/configs"
import { Modal } from "@/components/Modal"
import { ObfuscationFields } from "@/components/Obfuscation"
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
        <ObfuscationFields id="config" cover={draft.obfuscation} onChange={twist} />
      </Section>

      {error !== null && error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(error) as TextKey)}</div>
      )}
    </Modal>
  )
}
