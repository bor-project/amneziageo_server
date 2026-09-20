import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useRemoveRule, useRules } from "@/api/rules"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function RuleRemove() {
  const t = useText()
  const navigate = useNavigate()
  const { ruleId } = useParams()
  const rules = useRules()
  const remove = useRemoveRule()
  const all = rules.data ?? []
  const held = all.find((one) => one.id === Number(ruleId))

  useTail(held === undefined ? [] : [{ label: held.name }, { label: t("rules.remove") }])

  if (held === undefined) {
    return rules.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("rules.loading")}</div>
    ) : (
      <Navigate to="/routing/rules" replace />
    )
  }

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("rules.removeTitle", { name: held.name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        <div className="mt-1 text-[13px] text-muted">
          {held.targets.length > 0 ? held.targets.join(", ") : t("rules.anything")}
        </div>
      </div>

      {remove.error !== null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}

      <div className="flex justify-end gap-2">
        <Link to="/routing/rules" className={`flex h-10 items-center ${secondary}`}>
          {t("action.backToList")}
        </Link>
        <button
          type="button"
          onClick={() => void remove.mutateAsync(held.id).then(() => navigate("/routing/rules"))}
          disabled={remove.isPending}
          className={danger}
        >
          {t("rules.remove")}
        </button>
      </div>
    </div>
  )
}
