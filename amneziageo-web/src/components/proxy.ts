import { uncertified } from "@/api/proxies"
import type { Proxy, ProxyCertificate } from "@/api/proxies"
import type { Text } from "@/i18n"

export function proxyKind(t: Text, one: Proxy): string {
  return one.kind === "wg" ? t("proxies.kindWg") : t("proxies.kindWs")
}

export function proxyPoint(one: Proxy): string {
  return one.kind === "wg" ? one.target : `/${one.path}`
}

export function proxyFault(t: Text, one: Proxy, panel: ProxyCertificate | undefined): string {
  return one.message || (uncertified(panel, one) ? t("error.noCertificate") : "")
}
