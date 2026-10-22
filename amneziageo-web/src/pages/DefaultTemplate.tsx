import { useState } from "react"
import { useNavigate } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useClients } from "@/api/clients"
import { draftOf, useDnsCheck, useResolver } from "@/api/dns"
import { scopes } from "@/api/scopes"
import { useFreeClients, useTemplateDefaults, useTemplates } from "@/api/templates"
import type { TemplateDefaults } from "@/api/templates"
import { useTail } from "@/components/crumbs"
import { DnsOutcome, DnsServerChoice } from "@/components/DnsField"
import { Links, Part, Shown } from "@/components/fields"
import { clientItems } from "@/components/members"
import { footer, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"
import { useSpot } from "@/store/spots"

export function DefaultTemplate() {
  const t = useText()
  const navigate = useNavigate()
  const defaults = useTemplateDefaults().data
  const back = useSpot("/clients/templates")

  useTail([{ label: t("clients.noTemplate") }])

  if (defaults === undefined) {
    return <div className="mt-4 text-sm text-muted">{t("templates.loading")}</div>
  }

  return <DefaultForm defaults={defaults} onClose={() => navigate(back)} />
}

function DefaultForm({ defaults, onClose }: { defaults: TemplateDefaults; onClose: () => void }) {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const may = holds(user, scopes.manageClients)
  const templates = useTemplates().data ?? []
  const titles = new Map(templates.map((one): [number, string] => [one.id, one.name]))
  const members = clientItems(useClients().data ?? [], (one) =>
    one.templateId === null ? t("clients.noTemplate") : (titles.get(one.templateId) ?? ""),
  )
  const free = useFreeClients()
  const resolver = useResolver(false).data
  const check = useDnsCheck(resolver === undefined ? null : { dns: draftOf(resolver) })
  const lines = check.data === undefined ? undefined : { ...check.data, findings: [] }
  const start = defaults.clientIds.map(String)
  const [picked, setPicked] = useState(start)
  const edited = picked.join(",") !== start.join(",")

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("templates.partMain")}>
        <Shown id="template-name" caption={t("templates.name")} value={t("clients.noTemplate")} wide />
      </Part>

      <Part title={t("templates.partClients")}>
        <div className="sm:col-span-2">
          <Links
            id="template-clients"
            value={picked}
            items={members}
            placeholder={may ? t("templates.addClient") : t("clients.dash")}
            fixed={start}
            onChange={may ? setPicked : undefined}
          />
        </div>
      </Part>

      <Part title={t("templates.partRouting")}>
        <Shown id="template-entries" caption={t("templates.allowed")} value={defaults.allowedIps.join(", ")} wide />
      </Part>

      <Part title={t("templates.partNetwork")}>
        <DnsOutcome check={lines} caption="dnsCheck.lineTemplate" view="template">
          <DnsServerChoice mode={resolver?.filesMode} />
        </DnsOutcome>
        <Shown id="template-mtu" caption={t("templates.mtu")} value={String(defaults.mtu)} />
        <Shown id="template-keepalive" caption={t("templates.keepalive")} value={String(defaults.keepalive)} />
      </Part>

      {free.error !== null && <div className="text-sm text-alarm">{t(complaint(free.error))}</div>}

      <div className={footer}>
        {may ? (
          <>
            <button type="button" onClick={onClose} disabled={!edited || free.isPending} className={secondary}>
              {t("templates.cancel")}
            </button>
            <button
              type="button"
              onClick={() => void free.mutateAsync(picked.map(Number)).then(onClose)}
              disabled={!edited || free.isPending}
              className={primary}
            >
              {free.isPending ? t("templates.busy") : t("templates.save")}
            </button>
          </>
        ) : (
          <button type="button" onClick={onClose} className={secondary}>
            {t("action.backToList")}
          </button>
        )}
      </div>
    </div>
  )
}

