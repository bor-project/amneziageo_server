import { useNavigate } from "react-router-dom"
import { useTemplateDefaults } from "@/api/templates"
import { useTail } from "@/components/crumbs"
import { Part } from "@/components/fields"
import { card, field, label, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { lastSpot } from "@/store/spots"

export function DefaultTemplate() {
  const t = useText()
  const navigate = useNavigate()
  const defaults = useTemplateDefaults().data
  const back = lastSpot("connections", "/connections/templates")

  useTail([{ label: t("clients.noTemplate") }])

  if (defaults === undefined) {
    return <div className="mt-4 text-sm text-muted">{t("templates.loading")}</div>
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("templates.partMain")}>
        <Shown id="template-name" caption={t("templates.name")} value={t("clients.noTemplate")} wide />
      </Part>

      <Part title={t("templates.partRouting")}>
        <Shown id="template-entries" caption={t("templates.allowed")} value={defaults.allowedIps.join(", ")} wide />
        <Shown
          id="template-routing"
          caption={t("templates.routing")}
          value={t(defaults.routing ? "clients.routingOn" : "clients.routingOff")}
        />
      </Part>

      <Part title={t("templates.partNetwork")}>
        <Shown id="template-dns" caption={t("templates.dns")} value={defaults.dns.join(", ")} wide />
        <Shown id="template-mtu" caption={t("templates.mtu")} value={String(defaults.mtu)} />
        <Shown id="template-keepalive" caption={t("templates.keepalive")} value={String(defaults.keepalive)} />
      </Part>

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        <button type="button" onClick={() => navigate(back)} className={secondary}>
          {t("action.backToList")}
        </button>
      </div>
    </div>
  )
}

function Shown({ id, caption, value, wide = false }: { id: string; caption: string; value: string; wide?: boolean }) {
  return (
    <div className={wide ? "sm:col-span-2" : ""}>
      <label className={label} htmlFor={id}>
        {caption}
      </label>
      <input id={id} value={value} readOnly className={`mt-1 ${field}`} />
    </div>
  )
}
