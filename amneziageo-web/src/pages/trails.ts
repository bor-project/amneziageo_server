import type { Balancer } from "@/api/balancers"
import type { Client } from "@/api/clients"
import type { Config } from "@/api/configs"
import type { GeoSource } from "@/api/geo"
import type { Outbound } from "@/api/outbounds"
import type { Proxy } from "@/api/proxies"
import type { Rule } from "@/api/rules"
import type { Template } from "@/api/templates"
import type { Crumb } from "@/components/crumbs"

export function templateTrail(held: Template, list: Template[]): Crumb {
  return {
    label: held.name,
    options: list.map((one) => ({
      label: one.name,
      to: `/connections/templates/${one.id}`,
      mark: one.id === held.id,
    })),
  }
}

export function clientTrail(held: Client, list: Client[]): Crumb {
  return {
    label: held.name,
    options: list.map((one) => ({
      label: one.name,
      to: `/connections/clients/${one.id}`,
      mark: one.id === held.id,
    })),
  }
}

export function configTrail(held: Config, list: Config[]): Crumb {
  return {
    label: held.name,
    options: list.map((one) => ({
      label: one.name,
      to: `/connections/interfaces/${one.id}`,
      mark: one.id === held.id,
    })),
  }
}

export function proxyTrail(held: Proxy, list: Proxy[]): Crumb {
  return {
    label: held.name,
    options: list.map((one) => ({
      label: one.name,
      to: `/connections/proxies/${one.id}`,
      mark: one.id === held.id,
    })),
  }
}

export function ruleTrail(held: Rule, list: Rule[]): Crumb {
  return {
    label: held.name,
    options: list.map((one) => ({
      label: one.name,
      to: `/routing/rules/${one.id}/edit`,
      mark: one.id === held.id,
    })),
  }
}

export function outboundTrail(held: Outbound, list: Outbound[]): Crumb {
  return {
    label: held.name,
    options: list.map((one) => ({
      label: one.name,
      to: `/routing/channels/${one.id}/edit`,
      mark: one.id === held.id,
    })),
  }
}

export function balancerTrail(held: Balancer, list: Balancer[]): Crumb {
  return {
    label: held.name,
    options: list.map((one) => ({
      label: one.name,
      to: `/routing/channels/groups/${one.id}/edit`,
      mark: one.id === held.id,
    })),
  }
}

export function geoTrail(held: GeoSource, list: GeoSource[]): Crumb {
  return {
    label: held.name,
    options: list.map((one) => ({
      label: one.name,
      to: `/routing/geo/${one.id}/edit`,
      mark: one.id === held.id,
    })),
  }
}
