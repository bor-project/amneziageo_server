import { useState } from "react"
import { complaint, reason } from "@/api/auth"
import { scopes } from "@/api/scopes"
import { draftOf, useSaveSubscription, useSubscription } from "@/api/subscription"
import type { Subscription, SubscriptionDraft } from "@/api/subscription"
import { Multi, Row } from "@/components/fields"
import { card, field, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { same, subscriptionDrafted } from "@/store/draftSlice"
import { useAppDispatch, useAppSelector } from "@/store/hooks"

const own = "*"

export function Subscriptions() {
  const user = useAppSelector((s) => s.auth.user)
  const subscription = useSubscription()
  const may = holds(user, scopes.manageAccess)

  return subscription.data ? <Editor settings={subscription.data} may={may} /> : null
}

function Editor({ settings, may }: { settings: Subscription; may: boolean }) {
  const t = useText()
  const dispatch = useAppDispatch()
  const kept = useAppSelector((s) => s.drafts.subscription)
  const [fault, setFault] = useState<TextKey | null>(null)
  const save = useSaveSubscription()
  const saved = draftOf(settings)
  const draft = kept ?? saved
  const domain = domainOf(draft, settings.certificateRoot)
  const refused = fault ?? (kept === null && settings.fault.length > 0 ? reason(settings.fault) : null)

  function set(change: Partial<SubscriptionDraft>) {
    const next = { ...draft, ...change }
    dispatch(subscriptionDrafted(same(next, saved) ? null : next))
  }

  function pickDomain(picked: string) {
    if (picked === own) {
      set({ certificate: draft.certificate, certificateKey: draft.certificateKey })
      return
    }

    if (picked.length === 0) {
      set({ certificate: "", certificateKey: "" })
      return
    }

    set({
      certificate: `${settings.certificateRoot}/${picked}/fullchain.pem`,
      certificateKey: `${settings.certificateRoot}/${picked}/privkey.pem`,
    })
  }

  async function keep() {
    setFault(null)
    try {
      await save.mutateAsync(draft)
      dispatch(subscriptionDrafted(null))
    } catch (error) {
      setFault(complaint(error))
    }
  }

  function drop() {
    setFault(null)
    dispatch(subscriptionDrafted(null))
  }

  return (
    <div className={`mt-4 px-4 py-2 ${card}`}>
      <Row id="subscription-enabled" caption={t("subscription.enabled")}>
        <input
          id="subscription-enabled"
          type="checkbox"
          checked={draft.isEnabled}
          onChange={(e) => set({ isEnabled: e.target.checked })}
          className="size-4 accent-brand"
        />
      </Row>

      <Row id="subscription-listen" caption={t("settings.listen")}>
        <Multi
          id="subscription-listen"
          value={draft.listen}
          offers={settings.addresses}
          placeholder={t("settings.everyAddress")}
          onChange={(listen) => set({ listen })}
        />
      </Row>

      <Row id="subscription-domains" caption={t("settings.domains")}>
        <Multi
          id="subscription-domains"
          value={draft.domains}
          offers={settings.certificates}
          placeholder={t("settings.anyDomain")}
          onChange={(domains) => set({ domains })}
        />
      </Row>

      <Row id="subscription-port" caption={t("settings.port")}>
        <input
          id="subscription-port"
          type="number"
          className={field}
          value={draft.port}
          onChange={(e) => set({ port: Number(e.target.value) })}
        />
      </Row>

      <Row id="subscription-path" caption={t("subscription.path")}>
        <input
          id="subscription-path"
          className={field}
          value={draft.path}
          onChange={(e) => set({ path: e.target.value })}
        />
      </Row>

      <Row id="subscription-domain" caption={t("settings.domain")}>
        <select id="subscription-domain" className={field} value={domain} onChange={(e) => pickDomain(e.target.value)}>
          <option value="">{t("subscription.panelCertificate")}</option>
          {settings.certificates.map((name) => (
            <option key={name} value={name}>
              {name}
            </option>
          ))}
          <option value={own}>{t("settings.ownCertificate")}</option>
        </select>
      </Row>

      <Row id="subscription-certificate" caption={t("settings.certificate")}>
        <input
          id="subscription-certificate"
          className={field}
          value={draft.certificate}
          onChange={(e) => set({ certificate: e.target.value })}
        />
      </Row>

      <Row id="subscription-certificate-key" caption={t("settings.certificateKey")}>
        <input
          id="subscription-certificate-key"
          className={field}
          value={draft.certificateKey}
          onChange={(e) => set({ certificateKey: e.target.value })}
        />
      </Row>

      <Row id="subscription-hours" caption={t("subscription.updateHours")}>
        <input
          id="subscription-hours"
          type="number"
          className={field}
          value={draft.updateHours}
          onChange={(e) => set({ updateHours: Number(e.target.value) })}
        />
      </Row>

      <Row id="subscription-title" caption={t("subscription.title")}>
        <input
          id="subscription-title"
          className={field}
          value={draft.title}
          onChange={(e) => set({ title: e.target.value })}
        />
      </Row>

      <div className="flex items-center gap-2 border-t border-line py-3">
        <button
          type="button"
          className={primary}
          disabled={!may || kept === null || save.isPending}
          onClick={() => void keep()}
        >
          {t("settings.save")}
        </button>
        <button type="button" className={secondary} disabled={kept === null || save.isPending} onClick={drop}>
          {t("settings.cancel")}
        </button>
        {refused !== null && <span className="text-sm text-alarm">{t(refused)}</span>}
      </div>
    </div>
  )
}

function domainOf(draft: SubscriptionDraft, root: string) {
  if (draft.certificate.length === 0 && draft.certificateKey.length === 0) {
    return ""
  }

  const head = `${root}/`
  const name = draft.certificate.startsWith(head) ? draft.certificate.slice(head.length).split("/")[0] : ""

  return name.length > 0 &&
    draft.certificate === `${head}${name}/fullchain.pem` &&
    draft.certificateKey === `${head}${name}/privkey.pem`
    ? name
    : own
}
