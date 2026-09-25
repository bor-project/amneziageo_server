import { useState } from "react"
import { complaint, reason } from "@/api/auth"
import { outside, usePortState } from "@/api/firewall"
import { scopes } from "@/api/scopes"
import { draftOf, useSaveSubscription, useSubscription } from "@/api/subscription"
import type { Subscription, SubscriptionDraft } from "@/api/subscription"
import { Count, Flag, Line, Multi, Part, Pick } from "@/components/fields"
import { card, label, primary, secondary } from "@/components/styles"
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
  const port = usePortState(draft.port, draft.isEnabled && draft.separate && outside(draft.listen))
  const closed = port.data?.state === "closed"
  const blocked = closed && (draft.port !== saved.port || !saved.separate || !saved.isEnabled)
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
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("subscription.partServing")}>
        <div className="sm:col-span-2">
          <Flag
            id="subscription-enabled"
            caption={t("subscription.enabled")}
            value={draft.isEnabled}
            onChange={(isEnabled) => set({ isEnabled })}
          />
        </div>

        <div className="sm:col-span-2">
          <Flag
            id="subscription-separate"
            caption={t("subscription.separate")}
            value={draft.separate}
            onChange={(separate) => set({ separate })}
          />
        </div>

        {draft.separate && (
          <div className="sm:col-span-2">
            <label className={label} htmlFor="subscription-listen">
              {t("settings.listen")}
            </label>
            <div className="mt-1">
              <Multi
                id="subscription-listen"
                value={draft.listen}
                offers={settings.addresses}
                placeholder={t("settings.everyAddress")}
                onChange={(listen) => set({ listen })}
              />
            </div>
          </div>
        )}

        <div className="sm:col-span-2">
          <label className={label} htmlFor="subscription-domains">
            {t("settings.domains")}
          </label>
          <div className="mt-1">
            <Multi
              id="subscription-domains"
              value={draft.domains}
              offers={settings.certificates}
              placeholder={t("settings.anyDomain")}
              onChange={(domains) => set({ domains })}
            />
          </div>
        </div>

        {draft.separate && (
          <Count
            id="subscription-port"
            caption={t("settings.port")}
            value={draft.port}
            onChange={(port) => set({ port })}
            fault={closed ? t("settings.portClosed", { port: String(draft.port) }) : ""}
          />
        )}

        <Line
          id="subscription-path"
          caption={t("subscription.path")}
          value={draft.path}
          onChange={(path) => set({ path })}
        />

        <Count
          id="subscription-hours"
          caption={t("subscription.updateHours")}
          value={draft.updateHours}
          onChange={(updateHours) => set({ updateHours })}
        />

        <Line
          id="subscription-title"
          caption={t("subscription.title")}
          value={draft.title}
          onChange={(title) => set({ title })}
        />
      </Part>

      {draft.separate && (
        <Part title={t("settings.partCertificate")}>
          <Pick id="subscription-domain" caption={t("settings.domain")} value={domain} onChange={pickDomain}>
            <option value="">{t("subscription.panelCertificate")}</option>
            {settings.certificates.map((name) => (
              <option key={name} value={name}>
                {name}
              </option>
            ))}
            <option value={own}>{t("settings.ownCertificate")}</option>
          </Pick>

          <div />

          <Line
            id="subscription-certificate"
            caption={t("settings.certificate")}
            value={draft.certificate}
            onChange={(certificate) => set({ certificate })}
            wide
          />

          <Line
            id="subscription-certificate-key"
            caption={t("settings.certificateKey")}
            value={draft.certificateKey}
            onChange={(certificateKey) => set({ certificateKey })}
            wide
          />
        </Part>
      )}

      {refused !== null && <div className="text-sm text-alarm">{t(refused)}</div>}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        <button type="button" className={secondary} disabled={kept === null || save.isPending} onClick={drop}>
          {t("settings.cancel")}
        </button>
        <button
          type="button"
          className={primary}
          disabled={!may || kept === null || save.isPending || blocked}
          onClick={() => void keep()}
        >
          {t("settings.save")}
        </button>
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
