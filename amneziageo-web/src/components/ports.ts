import { useConfigs } from "@/api/configs"
import type { Config } from "@/api/configs"
import { usePanel } from "@/api/panel"
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

export interface PathHolder {
  port: number | null
  path: string
  name: string
}

export function useServicesHolders(): PortHolder[] {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const may = holds(user, scopes.manageAccess)
  const panel = usePanel(may).data
  const subscription = useSubscription(may).data
  const held: PortHolder[] = []

  if (subscription !== undefined && subscription.isEnabled && subscription.separate && subscription.port !== panel?.port) {
    held.push({ port: subscription.port, name: t("ports.subscription") })
  }

  return held
}

export function usePathHolders(): PathHolder[] {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const may = holds(user, scopes.manageAccess)
  const panel = usePanel(may).data
  const subscription = useSubscription(may).data
  const held: PathHolder[] = []

  if (panel !== undefined && bare(panel.path).length > 0) {
    held.push({ port: panel.port, path: bare(panel.path), name: t("ports.panel") })
  }

  if (subscription !== undefined && subscription.isEnabled && !subscription.separate) {
    held.push({ port: null, path: bare(subscription.path), name: t("ports.subscription") })
  }

  if (subscription !== undefined && subscription.isEnabled && subscription.separate && subscription.port === panel?.port) {
    held.push({ port: subscription.port, path: bare(subscription.path), name: t("ports.subscription") })
  }

  return held
}

export function useSocketHolders(mine: { config?: number } = {}): PathHolder[] {
  const t = useText()
  const configs = useConfigs().data ?? []

  return configs
    .filter((one) => one.id !== mine.config && one.webSocket)
    .map((one) => ({ port: servicesPort(one), path: one.webSocketPath, name: t("ports.webSocketOf", { name: one.name }) }))
}

export function usePortHolders(mine: { config?: number } = {}): PortHolder[] {
  const configs = useConfigs().data ?? []

  return configs.filter((one) => one.id !== mine.config).map((one) => ({ port: one.listenPort, name: one.name }))
}

export function portFault(t: Text, port: number, held: PortHolder[]): string {
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    return t("error.badPort")
  }

  const holder = held.find((one) => one.port === port)

  return holder === undefined ? "" : t("error.portBusy", { name: holder.name })
}

export function pathFault(t: Text, path: string, port: number | null, held: PathHolder[]): string {
  if (!/^[A-Za-z0-9_-]{1,64}$/.test(path)) {
    return t("error.badWebSocketPath")
  }

  const holder =
    port === null ? undefined : held.find((one) => (one.port === null || one.port === port) && same(one.path, path))

  return holder === undefined ? "" : t("error.pathTaken", { name: holder.name })
}

export function socketFault(t: Text, configs: Config[], port: number | null, path: string): string {
  const socket = configs.find(
    (one) => one.webSocket && (port === null || servicesPort(one) === port) && same(one.webSocketPath, bare(path)),
  )

  return socket === undefined ? "" : t("error.pathTaken", { name: t("ports.webSocketOf", { name: socket.name }) })
}

function servicesPort(config: Config): number {
  return config.servicesPort > 0 ? config.servicesPort : config.listenPort
}

function bare(path: string): string {
  return path.replace(/^\/+|\/+$/g, "")
}

function same(one: string, other: string): boolean {
  return one.length > 0 && one.toLowerCase() === other.toLowerCase()
}
