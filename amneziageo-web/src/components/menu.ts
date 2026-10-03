import { scopes } from "@/api/scopes"
import type { GlyphName } from "@/components/Glyph"
import type { TextKey } from "@/i18n"

export interface Item {
  to: string
  label: TextKey
  about?: TextKey
  icon: GlyphName
  scope: string
  add?: { to: string; scope: string }
}

export interface Section {
  to: string
  label: TextKey
  icon: GlyphName
  scope: string
  items: Item[]
}

export const interfaces: Item[] = [
  {
    to: "/interfaces",
    label: "tab.interfaces",
    icon: "shield",
    scope: scopes.readState,
    add: { to: "/interfaces/new", scope: scopes.manageInterfaces },
  },
]

// The tabs of the page of the clients: their configurations and the templates they are made from.
export const clients: Item[] = [
  {
    to: "/clients",
    label: "tab.configs",
    icon: "devices",
    scope: scopes.readState,
    add: { to: "/clients/new", scope: scopes.manageClients },
  },
  {
    to: "/clients/templates",
    label: "tab.templates",
    icon: "layout",
    scope: scopes.readState,
    add: { to: "/clients/templates/new", scope: scopes.manageClients },
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

// The overview leads the menu and every other part of the panel comes under it.
export const sections: Section[] = [
  { to: "/", label: "nav.overview", icon: "home", scope: scopes.readState, items: [] },
  { to: "/interfaces", label: "nav.interfaces", icon: "shield", scope: scopes.readState, items: [] },
  { to: "/clients", label: "nav.clients", icon: "devices", scope: scopes.readState, items: [] },
  { to: "/routing", label: "nav.routing", icon: "fork", scope: scopes.readState, items: routing },
  { to: "/settings", label: "nav.settings", icon: "gear", scope: scopes.manageAccess, items: settings },
]

// Finds the item a path belongs to, the one with the longest path when several hold it.
export function here(items: Item[], pathname: string): Item | undefined {
  return items
    .filter((one) => under(pathname, one.to))
    .reduce<Item | undefined>((best, one) => (best === undefined || one.to.length > best.to.length ? one : best), undefined)
}

export function under(path: string, to: string): boolean {
  return path === to || path.startsWith(`${to}/`) || path.startsWith(`${to}?`)
}
