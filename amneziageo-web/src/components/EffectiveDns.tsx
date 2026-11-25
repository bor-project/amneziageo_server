import { useBalancers } from "@/api/balancers"
import { useOutbounds } from "@/api/outbounds"
import type { EntryDraft } from "@/api/routingEntries"
import { useDnsDefaults } from "@/api/trafficRules"
import type { TrafficRuleDraft } from "@/api/trafficRules"
import type { DnsPolicy } from "@/components/dnsPolicy"
import { dnsChoice, dnsPolicy, dnsPresets } from "@/components/dnsPolicy"
import { useText } from "@/i18n"

export function DnsChoiceLabel({ rule }: { rule: TrafficRuleDraft }) {
  const t = useText()
  const choice = dnsChoice(rule)
  return <>{dnsPresets.find((one) => one.key === choice)?.name ?? t(choice === "default" ? "routing.dnsDefault" : "routing.dnsCustom")}</>
}

export function EffectiveDns({ rule, entry }: { rule: TrafficRuleDraft; entry: Pick<EntryDraft, "action" | "outbound"> }) {
  const t = useText()
  const outbounds = useOutbounds()
  const balancers = useBalancers()
  const defaults = useDnsDefaults()
  if (outbounds.data === undefined || balancers.data === undefined || defaults.data === undefined) {
    return <span className="text-muted">{t(outbounds.error || balancers.error || defaults.error ? "error.failed" : "page.loading")}</span>
  }
  const policy = dnsPolicy(rule, entry, outbounds.data, balancers.data, defaults.data.servers)
  return <DnsPolicyView policy={policy} />
}

export function DnsPolicyView({ policy }: { policy: DnsPolicy }) {
  const t = useText()
  if (policy.state === "blocked") { return <span className="text-muted">{t("routing.dnsUnused")}</span> }
  if (policy.state === "unavailable") { return <span className="text-alarm">{t("routing.dnsUnavailable")}</span> }

  return <div className="flex flex-col gap-1 text-xs">
    {policy.variant !== "single" && <span className="text-muted">{t(policy.variant === "round" ? "routing.dnsRound" : "routing.dnsSticky")}</span>}
    {policy.options.map((one, index) => <div key={one.path || index} className="break-words">
      {policy.options.length > 1 && <span className="text-muted">{one.path}: </span>}
      {one.servers.length > 0 ? one.servers.join(", ") : <span className="text-alarm">{t("routing.dnsMissing")}</span>}
    </div>)}
    {policy.state === "unknown" && <span className="text-muted">{t("outbounds.unknown")}</span>}
  </div>
}
