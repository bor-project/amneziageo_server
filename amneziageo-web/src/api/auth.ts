import axios from "axios"
import { bare, client } from "./client"
import { drop, keep } from "./tokens"
import type { TextKey } from "@/i18n/en"

export type Role = "admin" | "operator" | "viewer" | "none"

export interface Account {
  name: string
  displayName: string
  role: Role
  scheme: string
  scopes: string[]
}

export interface Session {
  access: string
  refresh: string | null
  expiresIn: number
  mustChangePassword: boolean
  user: Account
}

const complaints: Record<string, TextKey> = {
  "wrong-credentials": "error.wrongCredentials",
  "wrong-password": "error.wrongPassword",
  locked: "error.locked",
  disabled: "error.disabled",
  "no-password": "error.noPassword",
  weak: "error.weak",
  forbidden: "error.forbidden",
  incomplete: "error.incomplete",
  unauthorized: "error.sessionEnded",
  "session-ended": "error.sessionEnded",
  "refresh-expired": "error.sessionExpired",
  "refresh-replayed": "error.sessionEnded",
  "refresh-unknown": "error.sessionEnded",
  "bad-name": "error.badName",
  "name-taken": "error.nameTaken",
  "host-name": "error.hostName",
  "unknown-user": "error.unknownUser",
  "unknown-role": "error.unknownRole",
  "unknown-scope": "error.unknownScope",
  "unknown-config": "error.unknownConfig",
  "bad-interface-name": "error.badInterfaceName",
  "bad-host": "error.badHost",
  "bad-port": "error.badPort",
  "bad-address": "error.badAddress",
  "bad-allowed": "error.badAllowed",
  "bad-dns": "error.badDns",
  "bad-mtu": "error.badMtu",
  "bad-keepalive": "error.badKeepalive",
  "bad-key": "error.badKey",
  "bad-preshared": "error.badPreshared",
  "bad-junk": "error.badJunk",
  "bad-type": "error.badType",
  "bad-special": "error.badSpecial",
  "port-taken": "error.portTaken",
  invalid: "error.invalid",
  "builtin-role": "error.builtinRole",
  "role-in-use": "error.roleInUse",
  "last-admin": "error.lastAdmin",
  self: "error.self",
}

export async function signIn(user: string, password: string): Promise<Session> {
  const session = (await bare.post<Session>("/auth/login", { user, password })).data
  keep(session)

  return session
}

export async function changePassword(current: string, next: string): Promise<Session> {
  const session = (await client.post<Session>("/auth/password", { current, next })).data
  keep(session)

  return session
}

export async function whoAmI(): Promise<Account> {
  return (await client.get<Account>("/auth/me")).data
}

export async function signOut(): Promise<void> {
  try {
    await client.post("/auth/logout")
  } finally {
    drop()
  }
}

export function complaint(error: unknown): TextKey {
  if (axios.isAxiosError(error)) {
    const code = (error.response?.data as { error?: string } | undefined)?.error
    if (code && complaints[code]) {
      return complaints[code]
    }

    if (!error.response) {
      return "error.silent"
    }
  }

  return "error.failed"
}
