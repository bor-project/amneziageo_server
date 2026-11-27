import type { Balancer } from "@/api/balancers"
import type { Outbound } from "@/api/outbounds"
import type { EntryDraft } from "@/api/routingEntries"
import type { TrafficRuleDraft } from "@/api/trafficRules"

export const dnsPresets = [
  { key: "cloudflare", name: "Cloudflare", servers: ["1.1.1.1", "1.0.0.1"] },
  { key: "google", name: "Google", servers: ["8.8.8.8", "8.8.4.4"] },
  { key: "quad9", name: "Quad9", servers: ["9.9.9.9"] },
] as const

export type DnsChoice = "default" | "custom" | typeof dnsPresets[number]["key"]

type DnsFields = Pick<TrafficRuleDraft, "dnsMode" | "dns" | "dnsMembers">

export function dnsChoice(rule: DnsFields): DnsChoice {
  if (rule.dnsMode === "default") { return "default" }
  if (Object.keys(rule.dnsMembers ?? {}).length > 0) { return "custom" }
  return dnsPresets.find((one) => one.servers.length === rule.dns.length && one.servers.every((value, index) => value === rule.dns[index]))?.key ?? "custom"
}

export function dnsChosen(rule: DnsFields, choice: DnsChoice): DnsFields {
  if (choice === "default") { return { dnsMode: "default", dns: [], dnsMembers: {} } }
  const preset = dnsPresets.find((one) => one.key === choice)
  if (preset) { return { dnsMode: "custom", dns: [...preset.servers], dnsMembers: {} } }
  return { dnsMode: "custom", dns: rule.dnsMode === "custom" ? rule.dns : [], dnsMembers: rule.dnsMode === "custom" ? rule.dnsMembers : {} }
}

export interface DnsOption {
  path: string
  servers: string[]
}

export interface DnsPolicy {
  state: "resolved" | "blocked" | "unavailable" | "missing" | "unknown"
  choice: DnsChoice
  variant: "single" | "round" | "sticky"
  options: DnsOption[]
}

export function dnsPolicy(rule: DnsFields, entry: Pick<EntryDraft, "action" | "outbound">, outbounds: Outbound[], balancers: Balancer[], hostDns: string[]): DnsPolicy {
  const choice = dnsChoice(rule)
  const result = (state: DnsPolicy["state"], options: DnsOption[] = [], variant: DnsPolicy["variant"] = "single"): DnsPolicy => ({ state, choice, variant, options })
  if (entry.action === "block") { return result("blocked") }
  if (entry.action === "direct") {
    const servers = rule.dnsMode === "default" ? hostDns : rule.dns
    return result(servers.length > 0 ? "resolved" : "missing", [{ path: "", servers }])
  }
  const server = outbounds.find((one) => one.name === entry.outbound)
  if (server) {
    if (!server.isEnabled || server.state?.carries === false) { return result("unavailable") }
    const servers = rule.dnsMode === "default" ? server.dns?.length ? server.dns : server.kind === "local" ? hostDns : [] : rule.dnsMembers?.[server.name] ?? rule.dns
    return result(servers.length === 0 ? "missing" : server.state === null ? "unknown" : "resolved", [{ path: server.name, servers }])
  }
  const balancer = balancers.find((one) => one.name === entry.outbound)
  if (!balancer?.isEnabled) { return result("unavailable") }
  const live = balancer.members.flatMap((name) => {
    const member = balancer.state?.members.find((one) => one.name === name)
    const outbound = outbounds.find((one) => one.name === name)
    return member?.isEnabled && member.isAlive && outbound?.isEnabled && outbound.state?.carries !== false ? [outbound] : []
  })
  if (live.length === 0) { return result("unavailable") }
  const selected = balancer.strategy === "priority" ? live.slice(0, 1) : live
  const options = selected.map((one) => ({ path: one.name, servers: rule.dnsMode === "default"
    ? balancer.dns?.length ? balancer.dns : one.dns?.length ? one.dns : one.kind === "local" ? hostDns : []
    : rule.dnsMembers?.[one.name] ?? rule.dns }))
  return result(options.some((one) => one.servers.length === 0) ? "missing" : "resolved", options, balancer.strategy === "priority" ? "single" : balancer.strategy)
}
