import { scopes } from "@/api/scopes"
import type { GlyphName } from "@/components/Glyph"
import type { TextKey } from "@/i18n"
import { lastSpot, sectionOf } from "@/store/spots"

export interface Item {
  to: string
  label: TextKey
  about: TextKey
  icon: GlyphName
  scope: string
  add?: { to: string; scope: string }
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
    about: "about.interfaces",
    icon: "shield",
    scope: scopes.readState,
    add: { to: "/connections/interfaces/new", scope: scopes.manageInterfaces },
  },
  {
    to: "/connections/clients",
    label: "tab.clients",
    about: "about.clients",
    icon: "devices",
    scope: scopes.readState,
    add: { to: "/connections/clients/new", scope: scopes.manageClients },
  },
  {
    to: "/connections/templates",
    label: "tab.templates",
    about: "about.templates",
    icon: "layout",
    scope: scopes.readState,
    add: { to: "/connections/templates/new", scope: scopes.manageClients },
  },
]

export const routing: Item[] = [
  {
    to: "/routing/rules",
    label: "tab.rules",
    about: "about.rules",
    icon: "fork",
    scope: scopes.readState,
    add: { to: "/routing/rules/new", scope: scopes.manageRouting },
  },
  { to: "/routing/basic", label: "tab.basic", about: "about.basic", icon: "list", scope: scopes.readState },
  { to: "/routing/test", label: "tab.test", about: "about.test", icon: "flask", scope: scopes.readState },
  {
    to: "/routing/channels",
    label: "tab.channels",
    about: "about.channels",
    icon: "route",
    scope: scopes.readState,
    add: { to: "/routing/channels/new", scope: scopes.manageRouting },
  },
  {
    to: "/routing/geo",
    label: "tab.geo",
    about: "about.geo",
    icon: "map",
    scope: scopes.readState,
    add: { to: "/routing/geo/new", scope: scopes.manageRouting },
  },
  { to: "/routing/dns", label: "tab.dns", about: "about.dns", icon: "globe", scope: scopes.readState },
  { to: "/routing/ruleset", label: "tab.ruleset", about: "about.ruleset", icon: "wall", scope: scopes.manageRouting },
]

export const settings: Item[] = [
  { to: "/settings/server", label: "tab.server", about: "about.server", icon: "server", scope: scopes.manageAccess },
  {
    to: "/settings/certificates",
    label: "tab.certificates",
    about: "about.certificates",
    icon: "lock",
    scope: scopes.manageAccess,
  },
  {
    to: "/settings/subscriptions",
    label: "tab.subscriptions",
    about: "about.subscriptions",
    icon: "feed",
    scope: scopes.manageAccess,
  },
  { to: "/settings/users", label: "tab.users", about: "about.users", icon: "people", scope: scopes.manageAccess },
  {
    to: "/settings/diagnostics",
    label: "tab.diagnostics",
    about: "about.diagnostics",
    icon: "pulse",
    scope: scopes.manageAccess,
  },
]

export const sections: Section[] = [
  { to: "/", label: "nav.overview", scope: scopes.readState, items: [] },
  { to: "/connections", label: "nav.connections", scope: scopes.readState, items: connections },
  { to: "/settings", label: "nav.settings", scope: scopes.manageAccess, items: settings },
]

export function here(items: Item[], pathname: string): Item | undefined {
  return items.find((one) => under(pathname, one.to))
}

export function under(path: string, to: string): boolean {
  return path === to || path.startsWith(`${to}/`) || path.startsWith(`${to}?`)
}

export function place(to: string): string {
  const spot = lastSpot(sectionOf(to), to)

  return under(spot, to) ? spot : to
}
