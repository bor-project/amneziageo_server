import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useFailure } from "@/api/failure"
import { useRoutingEntries } from "@/api/routingEntries"
import { scopes } from "@/api/scopes"
import { useTrafficRules } from "@/api/trafficRules"
import { DnsChoiceLabel, EffectiveDns } from "@/components/EffectiveDns"
import { Find } from "@/components/fields"
import { RoutingSettings, RoutingToolbar } from "@/components/RoutingEditor"
import { RestoreRoutingDefaults } from "@/components/RestoreRoutingDefaults"
import { alarmLine, card } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Rules() {
  const t = useText()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const may = holds(useAppSelector((state) => state.auth.user), scopes.manageRouting)
  const rules = useTrafficRules()
  const entries = useRoutingEntries()
  const failed = useFailure(rules)
  const find = params.get("find") ?? ""
  const query = find.trim().toLowerCase()
  const shown = (rules.data ?? []).filter((one) => [one.name, ...one.targets, ...one.dns].some((text) => text.toLowerCase().includes(query)))

  function filter(value: string) {
    const next = new URLSearchParams(params)
    if (value === "") { next.delete("find") } else { next.set("find", value) }
    setParams(next, { replace: true })
  }

  return <>
    <RoutingToolbar onAdd={may && rules.isSuccess ? () => navigate("/routing/addresses/new") : undefined}
      actions={may ? <RestoreRoutingDefaults part="rules" /> : undefined}>
      <Find value={find} onChange={filter} className="w-full wide:w-72" />
    </RoutingToolbar>
    {failed !== null && <div className={alarmLine}>{t(complaint(failed))}</div>}
    {rules.isPending && <div className="p-4 text-sm text-muted">{t("rules.loading")}</div>}
    <div className="flex flex-col gap-3">
      {shown.map((one) => {
        const uses = (entries.data ?? []).filter((entry) => entry.ruleIds.includes(one.id))
        return <div key={one.id} className={`p-4 ${card}`}>
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0">
              {may ? <Link to={`/routing/addresses/${one.id}/edit`} className="font-semibold text-ink hover:text-brand-ink">{one.name}</Link> : one.name}
              <div className="mt-1 text-xs text-muted">{one.targets.length === 0 ? t("rules.anything") : one.targets.slice(0, 2).join(", ")}{one.targets.length > 2 ? ` +${one.targets.length - 2}` : ""}</div>
              <div className="mt-1 text-xs text-muted">DNS: <DnsChoiceLabel rule={one} /></div>
            </div>
            {may && <RoutingSettings onClick={() => navigate(`/routing/addresses/${one.id}/edit`)} />}
          </div>
          {uses.length > 0 && <div className="mt-3 border-t border-line pt-1">
            {uses.map((entry) => <div key={entry.id} className="grid gap-2 py-2 text-sm sm:grid-cols-2">
              <Link to={`/routing/rules/${entry.id}/edit`} className="text-brand-ink">{entry.name}</Link>
              <EffectiveDns rule={one} entry={entry} />
            </div>)}
          </div>}
          {uses.length === 0 && <div className="mt-3 text-xs text-muted">{t("routing.addressUnused")}</div>}
        </div>
      })}
    </div>
  </>
}
