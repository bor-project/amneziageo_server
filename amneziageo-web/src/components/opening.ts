import { useFirewall } from "@/api/firewall"
import type { FirewallReach } from "@/api/firewall"
import type { Text, TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

// Tells whether the panel may open a port for a holder of the right, and what the firewall of the host said.
export function useOpening(scope: string): { able: boolean; reach: FirewallReach | undefined; ufw: boolean } {
  const user = useAppSelector((s) => s.auth.user)
  const reach = useFirewall(holds(user, scope)).data

  return { able: reach?.able === true && holds(user, scope), reach, ufw: reach?.engine === "ufw" }
}

// Says how a closed port is opened by hand when the panel cannot open it.
export function manual(t: Text, reach: FirewallReach | undefined, port: string, key: TextKey): string {
  return reach?.reason === "host-ufw" ? t("ports.closedHostUfw", { port }) : t(key, { port })
}
