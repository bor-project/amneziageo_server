import type { Role } from "@/api/roles"

export function titleOf(roles: Role[], name: string): string {
  const found = roles.find((one) => one.name === name)

  return found !== undefined && found.title.length > 0 ? found.title : name
}

export function narrowest(roles: Role[]): string {
  const sorted = [...roles].sort((a, b) => a.scopes.length - b.scopes.length || a.name.localeCompare(b.name))

  return sorted[0]?.name ?? ""
}
