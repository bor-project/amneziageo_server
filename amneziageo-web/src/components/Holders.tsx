import { Link } from "react-router-dom"
import { useBalancers } from "@/api/balancers"
import { useRoutingEntries } from "@/api/routingEntries"
import { LinkLine, Links, Part } from "@/components/fields"
import { balancerItems, gone, ruleItems } from "@/components/members"
import { label } from "@/components/styles"
import { useText } from "@/i18n"

export function RuleLeave({ clients, inbounds }: { clients: string[]; inbounds: string[] }) {
  const t = useText()
  const { left, off } = gone(useRoutingEntries().data ?? [], clients, inbounds)

  return <>
    {left.length > 0 && <div className="text-[13px] text-muted">{`${t("clients.removeRules")}: `}<LinkLine items={ruleItems(left)} /></div>}
    {off.length > 0 && <div className="text-[13px] text-muted">{`${t("clients.removeRulesOff")}: `}<LinkLine items={ruleItems(off)} /></div>}
  </>
}

export function WayUse({ name, balancers, onBalancers }: { name: string; balancers?: number[]; onBalancers?: (balancers: number[]) => void }) {
  const t = useText()
  const all = useRoutingEntries().data ?? []
  const groups = useBalancers().data ?? []
  const through = all.filter((one) => one.action === "out" && one.outbound === name)
  const holding = groups.filter((one) => one.members.includes(name))

  return <Part title={t("outbounds.partUse")}>
    <div className="sm:col-span-2">
      <span className={label}>{t("routing.table")}</span>
      <div className="mt-1 flex flex-col gap-1 text-sm">
        {through.map((one) => <Link key={one.id} to={`/routing/rules/${one.id}/edit`} className="text-brand-ink">{one.name}</Link>)}
        {through.length === 0 && <span className="text-muted">—</span>}
      </div>
    </div>
    {onBalancers && <div className="sm:col-span-2">
      <span className={label}>{t("rules.balancers")}</span>
      <div className="mt-1"><Links id="way-balancers" value={(balancers ?? holding.map((one) => one.id)).map(String)}
        items={balancerItems(groups)} placeholder={t("outbounds.addBalancer")}
        fixed={holding.filter((one) => one.members.length === 1).map((one) => String(one.id))} onChange={(keys) => onBalancers(keys.map(Number))} /></div>
    </div>}
  </Part>
}

export function WayHolders({ name }: { name: string }) {
  const t = useText()
  const entries = (useRoutingEntries().data ?? []).filter((one) => one.action === "out" && one.outbound === name)
  const groups = (useBalancers().data ?? []).filter((one) => one.members.includes(name))

  return <>
    {entries.length > 0 && <div className="text-[13px] text-muted">{`${t("routing.table")}: `}<LinkLine items={ruleItems(entries)} /></div>}
    {groups.length > 0 && <div className="text-[13px] text-muted">{`${t("rules.balancers")}: `}<LinkLine items={balancerItems(groups)} /></div>}
  </>
}
