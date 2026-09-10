import { useState } from "react"
import { complaint } from "@/api/auth"
import { draftOf, usePanel, useSavePanel } from "@/api/panel"
import type { Panel, PanelDraft } from "@/api/panel"
import { scopes } from "@/api/scopes"
import { Multi, Row } from "@/components/fields"
import { card, field, primary, secondary } from "@/components/styles"
import { languageNames, useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { panelDrafted, same } from "@/store/draftSlice"
import { useAppDispatch, useAppSelector } from "@/store/hooks"

type Part = "server" | "certificates"

const own = "*"

export function PanelServer() {
  return <Settings part="server" />
}

export function PanelCertificates() {
  return <Settings part="certificates" />
}

function Settings({ part }: { part: Part }) {
  const user = useAppSelector((s) => s.auth.user)
  const panel = usePanel()
  const may = holds(user, scopes.manageAccess)

  return panel.data ? <Editor settings={panel.data} may={may} part={part} /> : null
}

function Editor({ settings, may, part }: { settings: Panel; may: boolean; part: Part }) {
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
    <div className={`mt-4 px-4 py-2 ${card}`}>
      {part === "server" ? (
        <>
          <Row id="panel-listen" caption={t("settings.listen")}>
            <Multi
              id="panel-listen"
              value={draft.listen}
              offers={settings.addresses}
              placeholder={t("settings.everyAddress")}
              onChange={(listen) => set({ listen })}
            />
          </Row>

          <Row id="panel-domains" caption={t("settings.domains")}>
            <Multi
              id="panel-domains"
              value={draft.domains}
              offers={settings.certificates}
              placeholder={t("settings.anyDomain")}
              onChange={(domains) => set({ domains })}
            />
          </Row>

          <Row id="panel-port" caption={t("settings.port")}>
            <input
              id="panel-port"
              type="number"
              className={field}
              value={draft.port}
              onChange={(e) => set({ port: Number(e.target.value) })}
            />
          </Row>

          <Row id="panel-path" caption={t("settings.path")}>
            <input id="panel-path" className={field} value={draft.path} onChange={(e) => set({ path: e.target.value })} />
          </Row>

          <Row id="panel-language" caption={t("settings.language")}>
            <select
              id="panel-language"
              className={field}
              value={draft.language}
              onChange={(e) => set({ language: e.target.value })}
            >
              <option value="auto">{t("language.auto")}</option>
              <option value="en">{languageNames.en}</option>
              <option value="ru">{languageNames.ru}</option>
            </select>
          </Row>
        </>
      ) : (
        <>
          <Row id="panel-domain" caption={t("settings.domain")}>
            <select id="panel-domain" className={field} value={domain} onChange={(e) => pickDomain(e.target.value)}>
              <option value="">{t("settings.noDomain")}</option>
              {settings.certificates.map((name) => (
                <option key={name} value={name}>
                  {name}
                </option>
              ))}
              <option value={own}>{t("settings.ownCertificate")}</option>
            </select>
          </Row>

          <Row id="panel-certificate" caption={t("settings.certificate")}>
            <input
              id="panel-certificate"
              className={field}
              value={draft.certificate}
              onChange={(e) => set({ certificate: e.target.value })}
            />
          </Row>

          <Row id="panel-certificate-key" caption={t("settings.certificateKey")}>
            <input
              id="panel-certificate-key"
              className={field}
              value={draft.certificateKey}
              onChange={(e) => set({ certificateKey: e.target.value })}
            />
          </Row>
        </>
      )}

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
        {fault !== null && <span className="text-sm text-alarm">{t(fault)}</span>}
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
