import { useState } from "react"
import { complaint } from "@/api/auth"
import { draftOf, usePanel, useSavePanel } from "@/api/panel"
import type { Panel, PanelDraft } from "@/api/panel"
import { scopes } from "@/api/scopes"
import { Count, Flag, Line, Multi, Part, Pick } from "@/components/fields"
import { card, label, primary, secondary } from "@/components/styles"
import { languageNames, useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { panelDrafted, same } from "@/store/draftSlice"
import { useAppDispatch, useAppSelector } from "@/store/hooks"

type Side = "server" | "certificates"

const own = "*"

export function PanelServer() {
  return <Settings part="server" />
}

export function PanelCertificates() {
  return <Settings part="certificates" />
}

function Settings({ part }: { part: Side }) {
  const user = useAppSelector((s) => s.auth.user)
  const panel = usePanel()
  const may = holds(user, scopes.manageAccess)

  return panel.data ? <Editor settings={panel.data} may={may} part={part} /> : null
}

function Editor({ settings, may, part }: { settings: Panel; may: boolean; part: Side }) {
  const t = useText()
  const dispatch = useAppDispatch()
  const kept = useAppSelector((s) => s.drafts.panel)
  const [fault, setFault] = useState<TextKey | null>(null)
  const save = useSavePanel()
  const saved = draftOf(settings)
  const draft = kept ?? saved
  const domain = domainOf(draft, settings.certificateRoot)

  function set(change: Partial<PanelDraft>) {
    const next = { ...draft, ...change }
    dispatch(panelDrafted(same(next, saved) ? null : next))
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
      dispatch(panelDrafted(null))
    } catch (error) {
      setFault(complaint(error))
    }
  }

  function drop() {
    setFault(null)
    dispatch(panelDrafted(null))
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      {part === "server" ? (
        <Part title={t("settings.partServer")}>
          <div className="sm:col-span-2">
            <label className={label} htmlFor="panel-listen">
              {t("settings.listen")}
            </label>
            <div className="mt-1">
              <Multi
                id="panel-listen"
                value={draft.listen}
                offers={settings.addresses}
                placeholder={t("settings.everyAddress")}
                onChange={(listen) => set({ listen })}
              />
            </div>
          </div>

          <div className="sm:col-span-2">
            <label className={label} htmlFor="panel-domains">
              {t("settings.domains")}
            </label>
            <div className="mt-1">
              <Multi
                id="panel-domains"
                value={draft.domains}
                offers={settings.certificates}
                placeholder={t("settings.anyDomain")}
                onChange={(domains) => set({ domains })}
              />
            </div>
          </div>

          <Count id="panel-port" caption={t("settings.port")} value={draft.port} onChange={(port) => set({ port })} />

          <Line id="panel-path" caption={t("settings.path")} value={draft.path} onChange={(path) => set({ path })} />

          <Pick
            id="panel-language"
            caption={t("settings.language")}
            value={draft.language}
            onChange={(language) => set({ language })}
          >
            <option value="auto">{t("language.auto")}</option>
            <option value="en">{languageNames.en}</option>
            <option value="ru">{languageNames.ru}</option>
          </Pick>

          <div className="sm:col-span-2">
            <Flag
              id="panel-opened"
              caption={t("settings.opened")}
              value={draft.opened}
              onChange={(opened) => set({ opened })}
            />
          </div>
        </Part>
      ) : (
        <Part title={t("settings.partCertificate")}>
          <Pick id="panel-domain" caption={t("settings.domain")} value={domain} onChange={pickDomain}>
            <option value="">{t("settings.noDomain")}</option>
            {settings.certificates.map((name) => (
              <option key={name} value={name}>
                {name}
              </option>
            ))}
            <option value={own}>{t("settings.ownCertificate")}</option>
          </Pick>

          <div />

          <Line
            id="panel-certificate"
            caption={t("settings.certificate")}
            value={draft.certificate}
            onChange={(certificate) => set({ certificate })}
            wide
          />

          <Line
            id="panel-certificate-key"
            caption={t("settings.certificateKey")}
            value={draft.certificateKey}
            onChange={(certificateKey) => set({ certificateKey })}
            wide
          />
        </Part>
      )}

      {fault !== null && <div className="text-sm text-alarm">{t(fault)}</div>}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        <button type="button" className={secondary} disabled={kept === null || save.isPending} onClick={drop}>
          {t("settings.cancel")}
        </button>
        <button
          type="button"
          className={primary}
          disabled={!may || kept === null || save.isPending}
          onClick={() => void keep()}
        >
          {t("settings.save")}
        </button>
      </div>
    </div>
  )
}

function domainOf(draft: PanelDraft, root: string) {
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
