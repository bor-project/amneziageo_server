import { useConfigs } from "@/api/configs"
import { usePanel } from "@/api/panel"
import { useProxies } from "@/api/proxies"
import { scopes } from "@/api/scopes"
import { useSubscription } from "@/api/subscription"
import { useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export interface PortHolder {
  port: number
  name: string
}

export function usePortHolders(mine: { config?: number; proxy?: number } = {}): PortHolder[] {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const may = holds(user, scopes.manageAccess)
  const configs = useConfigs().data ?? []
  const proxies = useProxies().data ?? []
  const panel = usePanel(may).data
  const subscription = useSubscription(may).data
  const held: PortHolder[] = []

  for (const one of configs) {
    if (one.id !== mine.config) {
      held.push({ port: one.listenPort, name: one.name })
    }
  }

  for (const one of proxies) {
    if (one.id !== mine.proxy) {
      held.push({ port: one.port, name: one.name })
    }
  }

  if (panel !== undefined) {
    held.push({ port: panel.port, name: t("ports.panel") })
  }

  if (subscription !== undefined && subscription.isEnabled) {
    held.push({ port: subscription.port, name: t("ports.subscription") })
  }

  return held
}

export function portFault(t: Text, port: number, held: PortHolder[]): string {
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    return t("error.badPort")
  }

  const holder = held.find((one) => one.port === port)

  return holder === undefined ? "" : t("error.portBusy", { name: holder.name })
}
