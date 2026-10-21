import { Link } from "react-router-dom"
import { useBalancers } from "@/api/balancers"
import { useResolver } from "@/api/dns"
import { useRules } from "@/api/rules"
import { LinkLine, Links, Part } from "@/components/fields"
import { balancerItems, gone, ruleItems } from "@/components/members"
import { label } from "@/components/styles"
import { useText } from "@/i18n"

// What the removal of clients and interfaces does to the rules that name them.
export function RuleLeave({ clients, inbounds }: { clients: string[]; inbounds: string[] }) {
  const t = useText()
  const { left, off } = gone(useRules().data ?? [], clients, inbounds)

  return (
    <>
      {left.length > 0 && (
        <div className="text-[13px] text-muted">
          {`${t("clients.removeRules")}: `}
          <LinkLine items={ruleItems(left)} />
        </div>
      )}
      {off.length > 0 && (
        <div className="text-[13px] text-muted">
          {`${t("clients.removeRulesOff")}: `}
          <LinkLine items={ruleItems(off)} />
        </div>
      )}
    </>
  )
}

// The rules and the groups a channel or a group is in, to change, and the DNS server that asks through it.
export function WayUse({
  name,
  rules,
  balancers,
  onRules,
  onBalancers,
}: {
  name: string
  rules?: number[]
  balancers?: number[]
  onRules: (rules: number[]) => void
  onBalancers?: (balancers: number[]) => void
}) {
  const t = useText()
  const all = useRules().data ?? []
  const groups = useBalancers().data ?? []
  const asks = useResolver(false).data?.outbound === name
  const through = all.filter((one) => one.action === "out" && one.outbound === name)
  const holding = groups.filter((one) => one.members.includes(name))

  return (
    <Part title={t("outbounds.partUse")}>
      <div className="sm:col-span-2">
        <span className={label}>{t("outbounds.rules")}</span>
        <div className="mt-1">
          <Links
            id="way-rules"
            value={(rules ?? through.map((one) => one.id)).map(String)}
            items={ruleItems(
              all.filter((one) => one.action === "out"),
              (one) => one.outbound,
            )}
            placeholder={t("outbounds.addRule")}
            fixed={through.map((one) => String(one.id))}
            onChange={(keys) => onRules(keys.map(Number))}
          />
        </div>
      </div>
      {onBalancers !== undefined && (
        <div className="sm:col-span-2">
          <span className={label}>{t("rules.balancers")}</span>
          <div className="mt-1">
            <Links
              id="way-balancers"
              value={(balancers ?? holding.map((one) => one.id)).map(String)}
              items={balancerItems(groups)}
              placeholder={t("outbounds.addBalancer")}
              fixed={holding.filter((one) => one.members.length === 1).map((one) => String(one.id))}
              onChange={(keys) => onBalancers(keys.map(Number))}
            />
          </div>
        </div>
      )}
      {asks && (
        <div className="sm:col-span-2">
          <span className={label}>{t("outbounds.resolver")}</span>
          <div className="mt-1 flex items-baseline gap-3 text-sm text-ink">
            {t("outbounds.dnsAsks", { name })}
            <Link to="/routing/dns" className="text-brand-ink hover:text-brand-lit">
              {t("action.goTo")}
            </Link>
          </div>
        </div>
      )}
    </Part>
  )
}

// The rules, the groups and the DNS server that leave through a channel or a group.
export function WayHolders({ name }: { name: string }) {
  const t = useText()
  const rules = (useRules().data ?? []).filter((one) => one.action === "out" && one.outbound === name)
  const groups = (useBalancers().data ?? []).filter((one) => one.members.includes(name))
  const asks = useResolver(false).data?.outbound === name

  return (
    <>
      {rules.length > 0 && (
        <div className="text-[13px] text-muted">
          {`${t("outbounds.rules")}: `}
          <LinkLine items={ruleItems(rules)} />
        </div>
      )}
      {groups.length > 0 && (
        <div className="text-[13px] text-muted">
          {`${t("rules.balancers")}: `}
          <LinkLine items={balancerItems(groups)} />
        </div>
      )}
      {asks && (
        <div className="text-[13px] text-muted">
          <Link to="/routing/dns" className="text-brand-ink hover:text-brand-lit">
            {t("outbounds.resolver")}
          </Link>
        </div>
      )}
    </>
  )
}
