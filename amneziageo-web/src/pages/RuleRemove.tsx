import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useRoutingEntries } from "@/api/routingEntries"
import { useRemoveTrafficRule, useTrafficRules } from "@/api/trafficRules"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function RuleRemove() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/addresses")
  const { ruleId } = useParams()
  const rules = useTrafficRules()
  const remove = useRemoveTrafficRule()
  const held = rules.data?.find((one) => one.id === Number(ruleId))
  const entries = (useRoutingEntries().data ?? []).filter((one) => one.ruleIds.includes(Number(ruleId)))
  useTail(held === undefined ? [] : [{ label: held.name, to: `/routing/addresses/${held.id}/edit` }, { label: t("rules.remove") }])

  if (held === undefined) {
    return rules.data === undefined ? <div className="mt-4 text-sm text-muted">{t("rules.loading")}</div> : <Navigate to={back} replace />
  }

  return <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
    <h2 className="text-[22px] font-semibold">{t("rules.removeTitle", { name: held.name })}</h2>
    <div className={`border-alarm-line p-4 ${card}`}>
      <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
      <div className="mt-1 text-sm text-muted">{held.targets.join(", ") || t("rules.anything")}</div>
      {entries.length > 0 && <div className="mt-3 flex flex-col gap-1 text-sm">
        <span>{t("routing.usedByEntries")}</span>
        {entries.map((one) => <Link key={one.id} to={`/routing/rules/${one.id}/edit`} className="text-brand-ink">{one.name}</Link>)}
      </div>}
    </div>
    {remove.error != null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}
    <div className="flex justify-end gap-2">
      <Link to={back} className={`flex h-10 items-center ${secondary}`}>{t("action.backToList")}</Link>
      <button type="button" className={danger} disabled={remove.isPending || entries.length > 0}
        onClick={() => void remove.mutateAsync(held.id).then(() => navigate(back)).catch(() => undefined)}>{t("rules.remove")}</button>
    </div>
  </div>
}
