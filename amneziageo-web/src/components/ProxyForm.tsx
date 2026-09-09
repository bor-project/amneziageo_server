import { useState } from "react"
import { complaint } from "@/api/auth"
import type { ProxyCertificate, ProxyDraft } from "@/api/proxies"
import { Count, Flag, Line, Section } from "@/components/fields"
import { Modal } from "@/components/Modal"
import { primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function ProxyForm({
  title,
  start,
  panel,
  pending,
  error,
  onSave,
  onClose,
}: {
  title: string
  start: ProxyDraft
  panel: ProxyCertificate | undefined
  pending: boolean
  error: unknown
  onSave: (draft: ProxyDraft) => void
  onClose: () => void
}) {
  const t = useText()
  const [draft, setDraft] = useState<ProxyDraft>(start)

  function put(part: Partial<ProxyDraft>) {
    setDraft({ ...draft, ...part })
  }

  return (
    <Modal
      title={title}
      onClose={onClose}
      wide
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("proxies.cancel")}
          </button>
          <button type="button" onClick={() => onSave(draft)} disabled={pending} className={primary}>
            {t("proxies.save")}
          </button>
        </>
      }
    >
      <Section title={t("proxies.serving")}>
        <Line id="proxy-name" caption={t("proxies.name")} value={draft.name} onChange={(v) => put({ name: v })} />
        <Count id="proxy-port" caption={t("proxies.port")} value={draft.port} onChange={(v) => put({ port: v })} />
        <Line id="proxy-path" caption={t("proxies.path")} value={draft.path} onChange={(v) => put({ path: v })} wide />
        <div className="col-span-2">
          <Flag
            id="proxy-on"
            caption={t("proxies.enabled")}
            value={draft.isEnabled}
            onChange={(v) => put({ isEnabled: v })}
          />
        </div>
      </Section>

      <Section title={t("proxies.tls")}>
        <Line
          id="proxy-certificate"
          caption={t("proxies.certificate")}
          value={draft.certificate}
          onChange={(v) => put({ certificate: v })}
          placeholder={panel?.chain ?? ""}
          wide
        />
        <Line
          id="proxy-certificate-key"
          caption={t("proxies.certificateKey")}
          value={draft.certificateKey}
          onChange={(v) => put({ certificateKey: v })}
          placeholder={panel?.key ?? ""}
          wide
        />
      </Section>

      {error !== null && error !== undefined && <div className="text-sm text-alarm">{t(complaint(error))}</div>}
    </Modal>
  )
}
