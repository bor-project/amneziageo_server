import type { ApiToken } from "@/api/apiTokens"
import type { Balancer } from "@/api/balancers"
import type { Client } from "@/api/clients"
import type { Config } from "@/api/configs"
import type { Outbound } from "@/api/outbounds"
import type { Preset } from "@/api/presets"
import type { Role } from "@/api/roles"
import type { Rule } from "@/api/rules"
import type { Template } from "@/api/templates"
import type { User } from "@/api/users"
import type { LinkItem } from "@/components/fields"
import { titleOf } from "@/components/roles"

// The list of a rule a client or an interface is named in.
export type Side = "clients" | "inbounds"

// Links to clients by name, each with what the note says of it.
export function clientItems(clients: Client[], note?: (one: Client) => string): LinkItem[] {
  return [...clients]
    .sort((a, b) => a.name.localeCompare(b.name))
    .map((one) => ({
      key: String(one.id),
      name: one.name,
      to: `/clients/${one.id}/settings`,
      ...(note === undefined ? {} : { note: note(one) }),
    }))
}

// Links to templates by name, each with what the note says of it.
export function templateItems(templates: Template[], note?: (one: Template) => string): LinkItem[] {
  return [...templates]
    .sort((a, b) => a.name.localeCompare(b.name))
    .map((one) => ({
      key: String(one.id),
      name: one.name,
      to: `/clients/templates/${one.id}/edit`,
      ...(note === undefined ? {} : { note: note(one) }),
    }))
}

// Links to presets by name.
export function presetItems(presets: Preset[]): LinkItem[] {
  return [...presets]
    .sort((a, b) => a.name.localeCompare(b.name))
    .map((one) => ({ key: String(one.id), name: one.name, to: `/clients/presets/${one.id}/edit` }))
}

// Links to rules in their order, each with what the note and the mark say of it.
export function ruleItems(
  rules: Rule[],
  note?: (one: Rule) => string | undefined,
  mark?: (one: Rule) => string | undefined,
): LinkItem[] {
  return [...rules]
    .sort((a, b) => a.position - b.position)
    .map((one) => {
      const said = note?.(one)
      const marked = mark?.(one)

      return {
        key: String(one.id),
        name: one.name,
        to: `/routing/rules/${one.id}/edit`,
        ...(said === undefined ? {} : { note: said }),
        ...(marked === undefined ? {} : { mark: marked }),
      }
    })
}

// Links to groups by name.
export function balancerItems(balancers: Balancer[]): LinkItem[] {
  return [...balancers]
    .sort((a, b) => a.name.localeCompare(b.name))
    .map((one) => ({ key: String(one.id), name: one.name, to: `/routing/channels/groups/${one.id}/edit` }))
}

// Links to accounts by name, each with the title of the role it holds.
export function userItems(users: User[], roles: Role[], none: string): LinkItem[] {
  return [...users]
    .sort((a, b) => a.name.localeCompare(b.name))
    .map((one) => ({
      key: one.name,
      name: one.name,
      to: `/settings/users/${encodeURIComponent(one.name)}/edit`,
      note: one.role.length === 0 ? none : titleOf(roles, one.role),
    }))
}

// Names of tokens, which have no page of their own.
export function tokenItems(tokens: ApiToken[]): LinkItem[] {
  return [...tokens]
    .sort((a, b) => a.name.localeCompare(b.name))
    .map((one) => ({ key: String(one.id), name: one.name }))
}

// Returns the page of a channel or a group by name, none when the panel holds neither.
export function wayTo(name: string, outbounds: Outbound[], balancers: Balancer[]): string | undefined {
  const outbound = outbounds.find((one) => one.name === name)
  if (outbound !== undefined) {
    return `/routing/channels/${outbound.id}/edit`
  }

  const balancer = balancers.find((one) => one.name === name)

  return balancer === undefined ? undefined : `/routing/channels/groups/${balancer.id}/edit`
}

// Returns the page of a client by name, whatever the case.
export function clientTo(name: string, clients: Client[]): string | undefined {
  const found = clients.find((one) => one.name.toLowerCase() === name.toLowerCase())

  return found === undefined ? undefined : `/clients/${found.id}/settings`
}

// Returns the page of an interface by name.
export function configTo(name: string, configs: Config[]): string | undefined {
  const found = configs.find((one) => one.name === name)

  return found === undefined ? undefined : `/interfaces/${found.id}/edit`
}

// Tells whether a rule names the client or the interface: clients whatever the case, interfaces exactly.
export function names(rule: Rule, side: Side, name: string): boolean {
  return side === "clients"
    ? rule.clients.some((one) => one.toLowerCase() === name.toLowerCase())
    : rule.inbounds.includes(name)
}

// Returns the rules the removal of clients and interfaces takes the names off and the rules it turns off.
export function gone(rules: Rule[], clients: string[], inbounds: string[]): { left: Rule[]; off: Rule[] } {
  const lowered = clients.map((one) => one.toLowerCase())
  const left: Rule[] = []
  const off: Rule[] = []
  for (const rule of rules) {
    const keptClients = rule.clients.filter((one) => !lowered.includes(one.toLowerCase()))
    const keptInbounds = rule.inbounds.filter((one) => !inbounds.includes(one))
    const lostClients = keptClients.length < rule.clients.length
    const lostInbounds = keptInbounds.length < rule.inbounds.length
    if (!lostClients && !lostInbounds) {
      continue
    }

    if ((lostClients && keptClients.length === 0) || (lostInbounds && keptInbounds.length === 0)) {
      off.push(rule)
    } else {
      left.push(rule)
    }
  }

  return { left, off }
}

// Returns the first names of a list and how many more there are.
export function few(values: string[], more: (count: number) => string): string {
  const head = values.slice(0, 3).join(", ")

  return values.length > 3 ? `${head} ${more(values.length - 3)}` : head
}
