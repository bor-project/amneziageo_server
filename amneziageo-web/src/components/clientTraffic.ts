import type { Client } from "@/api/clients"

export function trafficView(one: Client, total: boolean) {
  const limits = one.state.limits?.policy.enabled ? one.state.limits : null
  const quota = limits ? limits.policy.quotaBytes : one.dailyLimit
  const cumulative = quota <= 0 || total

  return {
    quota,
    cumulative,
    value: cumulative ? one.state.total?.totalBytes ?? null : limits?.usedBytes ?? one.state.used,
  }
}
