import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useInterfaceTemplates, useRemoveInterfaceTemplate } from "@/api/interfaceTemplates"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"

const list = "/connections/templates/interfaces"

export function InterfaceTemplateRemove() {
  const t = useText()
  const navigate = useNavigate()
  const { templateId } = useParams()
  const templates = useInterfaceTemplates()
  const remove = useRemoveInterfaceTemplate()
  const held = templates.data?.find((one) => one.id === Number(templateId))

  useTail(held === undefined ? [] : [{ label: held.name }, { label: t("templates.remove") }])

  if (held === undefined) {
    return templates.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("templates.loading")}</div>
    ) : (
      <Navigate to={list} replace />
    )
  }

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("templates.removeTitle", { name: held.name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        <div className="mt-1 text-[13px] text-muted">
          {t("templates.configsCount", { count: String(held.configs) })}
        </div>
      </div>

      {remove.error !== null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}

      <div className="flex justify-end gap-2">
        <Link to={list} className={`flex h-10 items-center ${secondary}`}>
          {t("action.backToList")}
        </Link>
        <button
          type="button"
          onClick={() => void remove.mutateAsync(held.id).then(() => navigate(list))}
          disabled={remove.isPending}
          className={danger}
        >
          {t("templates.remove")}
        </button>
      </div>
    </div>
  )
}
