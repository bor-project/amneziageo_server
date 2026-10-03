import { useState } from "react"
import { complaint } from "@/api/auth"
import { useConfigs } from "@/api/configs"
import { useFailure } from "@/api/failure"
import { outside, usePortState } from "@/api/firewall"
import type { Holding } from "@/api/firewall"
import { draftOf, useNameSample, usePanel, useSavePanel } from "@/api/panel"
import type { Panel, PanelDraft } from "@/api/panel"
import { scopes } from "@/api/scopes"
import { KeepQuestion, PortNote } from "@/components/PortNote"
import { Count, Flag, Help, Line, Multi, Part, Pick } from "@/components/fields"
import { defaultName, fillName, unknownKeys } from "@/components/names"
import { manual, useOpening } from "@/components/opening"
import { socketFault } from "@/components/ports"
import { footer, label, primary, secondary } from "@/components/styles"
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
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const panel = usePanel()
  const failed = useFailure(panel)
  const may = holds(user, scopes.manageAccess)

  if (panel.data === undefined) {
    return failed !== null ? (
      <div className="mt-4 text-sm text-alarm">{t(complaint(failed))}</div>
    ) : (
      <div className="mt-4 text-sm text-muted">{t("page.loading")}</div>
    )
  }

  return <Editor settings={panel.data} may={may} part={part} />
}

function Editor({ settings, may, part }: { settings: Panel; may: boolean; part: Side }) {
  const t = useText()
  const dispatch = useAppDispatch()
  const kept = useAppSelector((s) => s.drafts.panel)
  const [fault, setFault] = useState<TextKey | null>(null)
  const save = useSavePanel()
  const names = useNameSample(part === "server")
  const saved = draftOf(settings)
  const draft = kept ?? saved
  const { able, reach, ufw } = useOpening(scopes.manageAccess)
  const [opening, setOpening] = useState(false)
  const [asking, setAsking] = useState(false)
  const moved = draft.port !== saved.port
  const ahead = able && settings.opened && moved
  const port = usePortState(draft.port, part === "server" && outside(draft.listen) && !ahead && !opening)
  const closed = port.data?.state === "closed"
  const blocked = closed && moved && !ahead && !opening
  const previous = settings.opened && moved && outside(saved.listen) ? [`${saved.port}/tcp`] : []
  const portLabel = `${draft.port}/tcp`
  const configs = useConfigs().data ?? []
  const socket = socketFault(t, configs, draft.port, draft.path)
  const domain = domainOf(draft, settings.certificateRoot)
  const template = draft.nameTemplate.trim() || defaultName
  const strange = unknownKeys(template)
  const sample =
    strange.length === 0 && names.data !== undefined && names.data.stamp.length > 0
      ? fillName(template, names.data.values, names.data.stamp)
      : ""

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

  async function keep(holding: Holding) {
    setFault(null)
    try {
      await save.mutateAsync({ ...draft, ...holding })
      setOpening(false)
      dispatch(panelDrafted(null))
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

          <Count
            id="panel-port"
            caption={t("settings.port")}
            value={draft.port}
            onChange={(port) => set({ port })}
            note={
              outside(draft.listen) && (
                <PortNote
                  port={portLabel}
                  state={port.data?.state}
                  ahead={ahead}
                  able={able}
                  manual={manual(t, reach, portLabel, "settings.portClosed")}
                  owner={moved ? null : { kind: "panel" }}
                  staged={opening}
                  onStage={setOpening}
                />
              )
            }
          />

          <Line
            id="panel-path"
            caption={t("settings.path")}
            value={draft.path}
            onChange={(path) => set({ path })}
            fault={socket}
          />

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

          <Line
            id="panel-name-template"
            caption={
              <span className="flex items-center gap-1">
                {t("settings.nameTemplate")}
                <Help text={t("settings.nameKeys")} />
              </span>
            }
            value={draft.nameTemplate}
            placeholder={defaultName}
            onChange={(nameTemplate) => set({ nameTemplate })}
            preview={sample}
            fault={strange.length > 0 ? t("settings.nameUnknown", { list: strange.join(", ") }) : ""}
          />

          <div className="sm:col-span-2">
            <Flag
              id="panel-prereleases"
              caption={t("settings.prereleases")}
              value={draft.prereleases}
              onChange={(prereleases) => set({ prereleases })}
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
