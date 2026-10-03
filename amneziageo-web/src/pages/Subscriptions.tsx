import { useState } from "react"
import { complaint, reason } from "@/api/auth"
import { useConfigs } from "@/api/configs"
import { outside, usePortState } from "@/api/firewall"
import type { Holding } from "@/api/firewall"
import { usePanel } from "@/api/panel"
import { scopes } from "@/api/scopes"
import { draftOf, useSaveSubscription, useSubscription } from "@/api/subscription"
import type { Subscription, SubscriptionDraft } from "@/api/subscription"
import { KeepQuestion, PortNote } from "@/components/PortNote"
import { Count, Flag, Line, Multi, Part, Pick } from "@/components/fields"
import { manual, useOpening } from "@/components/opening"
import { socketFault } from "@/components/ports"
import { footer, label, primary, secondary } from "@/components/styles"
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
  const { able, reach, ufw } = useOpening(scopes.manageAccess)
  const [opening, setOpening] = useState(false)
  const [asking, setAsking] = useState(false)
  const served = saved.isEnabled && saved.separate
  const moved = draft.port !== saved.port || !served
  const ahead = able && settings.opened && moved
  const port = usePortState(
    draft.port,
    draft.isEnabled && draft.separate && outside(draft.listen) && !ahead && !opening,
  )
  const closed = port.data?.state === "closed"
  const blocked = closed && moved && !ahead && !opening
  const previous =
    settings.opened && served && draft.isEnabled && draft.separate && draft.port !== saved.port && outside(saved.listen)
      ? [`${saved.port}/tcp`]
      : []
  const portLabel = `${draft.port}/tcp`
  const configs = useConfigs().data ?? []
  const panel = usePanel(may).data
  const where = draft.separate ? draft.port : null
  const shared = !draft.separate || draft.port === panel?.port
  const socket = draft.isEnabled && shared ? socketFault(t, configs, where, draft.path) : ""
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

  async function keep(holding: Holding) {
    setFault(null)
    try {
      await save.mutateAsync({ ...draft, ...holding })
      setOpening(false)
      dispatch(subscriptionDrafted(null))
    } catch (error) {
      setFault(complaint(error))
    }
  }

  // Saves, asking first whether the port the panel held open before it moved is closed.
  function go() {
    if (previous.length > 0 && able && ufw) {
      setAsking(true)
      return
    }

    void keep(opening ? { opened: true } : {})
  }

  function drop() {
    setFault(null)
    setOpening(false)
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
            note={
              draft.isEnabled &&
              outside(draft.listen) && (
                <PortNote
                  port={portLabel}
                  state={port.data?.state}
                  ahead={ahead}
                  able={able}
                  manual={manual(t, reach, portLabel, "settings.portClosed")}
                  owner={moved ? null : { kind: "subscriptions" }}
                  staged={opening}
                  onStage={setOpening}
                />
              )
            }
          />
        )}

        <Line
          id="subscription-path"
          caption={t("subscription.path")}
          value={draft.path}
          onChange={(path) => set({ path })}
          fault={socket}
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

      <div className={footer}>
        <button
          type="button"
          className={secondary}
          disabled={(kept === null && !opening) || save.isPending}
          onClick={drop}
        >
          {t("settings.cancel")}
        </button>
        <button
          type="button"
          className={primary}
          disabled={!may || (kept === null && !opening) || save.isPending || blocked || socket.length > 0}
          onClick={go}
        >
          {t("settings.save")}
        </button>
      </div>

      {asking && (
        <KeepQuestion
          ports={previous}
          onCancel={() => setAsking(false)}
          onAnswer={(keeping) => {
            setAsking(false)
            void keep({ keep: keeping })
          }}
        />
      )}
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
