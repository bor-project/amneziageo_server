import { scopes } from "@/api/scopes"
import type { TextKey } from "@/i18n"

export interface Item {
  to: string
  label: TextKey
  scope: string
  add?: { to: string; scope: string }
  kids?: Item[]
}

export interface Section {
  to: string
  label: TextKey
  scope: string
  items: Item[]
}

export const connections: Item[] = [
  {
    to: "/connections/interfaces",
    label: "tab.interfaces",
    scope: scopes.readState,
    add: { to: "/connections/interfaces/new", scope: scopes.manageInterfaces },
  },
  {
    to: "/connections/clients",
    label: "tab.clients",
    scope: scopes.readState,
    add: { to: "/connections/clients/new", scope: scopes.manageClients },
  },
  {
    to: "/connections/templates",
    label: "tab.templates",
    scope: scopes.readState,
    kids: [
      {
        to: "/connections/templates/clients",
        label: "tab.templateClients",
        scope: scopes.readState,
        add: { to: "/connections/templates/clients/new", scope: scopes.manageClients },
      },
      {
        to: "/connections/templates/interfaces",
        label: "tab.templateInterfaces",
        scope: scopes.readState,
        add: { to: "/connections/templates/interfaces/new", scope: scopes.manageInterfaces },
      },
      {
        to: "/connections/templates/proxies",
        label: "tab.templateProxies",
        scope: scopes.readState,
        add: { to: "/connections/templates/proxies/new", scope: scopes.manageRouting },
      },
    ],
  },
  {
    to: "/connections/proxies",
    label: "tab.proxies",
    scope: scopes.readState,
    add: { to: "/connections/proxies/new", scope: scopes.manageRouting },
  },
]

export const routing: Item[] = [
  {
    to: "/routing/rules",
    label: "tab.rules",
    scope: scopes.readState,
    add: { to: "/routing/rules/new", scope: scopes.manageRouting },
  },
  { to: "/routing/basic", label: "tab.basic", scope: scopes.readState },
  { to: "/routing/test", label: "tab.test", scope: scopes.readState },
  {
    to: "/routing/channels",
    label: "tab.channels",
    scope: scopes.readState,
    add: { to: "/routing/channels/new", scope: scopes.manageRouting },
  },
  {
    to: "/routing/geo",
    label: "tab.geo",
    scope: scopes.readState,
    add: { to: "/routing/geo/new", scope: scopes.manageRouting },
  },
  { to: "/routing/dns", label: "tab.dns", scope: scopes.readState },
  { to: "/routing/ruleset", label: "tab.ruleset", scope: scopes.manageRouting },
]

export const settings: Item[] = [
  { to: "/settings/server", label: "tab.server", scope: scopes.manageAccess },
  { to: "/settings/certificates", label: "tab.certificates", scope: scopes.manageAccess },
  { to: "/settings/subscriptions", label: "tab.subscriptions", scope: scopes.manageAccess },
  { to: "/settings/users", label: "tab.users", scope: scopes.manageAccess },
  { to: "/settings/diagnostics", label: "tab.diagnostics", scope: scopes.manageAccess },
]

export const sections: Section[] = [
  { to: "/", label: "nav.overview", scope: scopes.readState, items: [] },
  { to: "/connections", label: "nav.connections", scope: scopes.readState, items: connections },
  { to: "/routing", label: "nav.routing", scope: scopes.readState, items: routing },
  { to: "/settings", label: "nav.settings", scope: scopes.manageAccess, items: settings },
]

export function here(items: Item[], pathname: string): Item | undefined {
  return items.find((one) => under(pathname, one.to))
}

export function under(path: string, to: string): boolean {
  return path === to || path.startsWith(`${to}/`) || path.startsWith(`${to}?`)
}
