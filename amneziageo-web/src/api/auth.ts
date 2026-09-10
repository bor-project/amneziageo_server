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
  "bad-client-name": "error.badClientName",
  "bad-client-key": "error.badClientKey",
  "bad-client-preshared": "error.badClientPreshared",
  "bad-client-address": "error.badClientAddress",
  "bad-client-note": "error.badClientNote",
  "client-name-taken": "error.clientNameTaken",
  "client-key-taken": "error.clientKeyTaken",
  "client-address-taken": "error.clientAddressTaken",
  "client-address-outside": "error.clientAddressOutside",
  "client-address-reserved": "error.clientAddressReserved",
  "unknown-client": "error.unknownClient",
  "bad-template-name": "error.badTemplateName",
  "unknown-template": "error.unknownTemplate",
  "template-in-use": "error.templateInUse",
  "bad-entry": "error.badEntry",
  "too-many-entries": "error.tooManyEntries",
  "bad-interface-name": "error.badInterfaceName",
  "bad-host": "error.badHost",
  "no-certificate": "error.noCertificate",
  "unknown-proxy": "error.unknownProxy",
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
  "bad-span": "error.badSpan",
  "bad-blocked": "error.badBlocked",
  "bad-header-key": "error.badHeaderKey",
  "port-taken": "error.portTaken",
  "bad-kind": "error.badKind",
  "bad-url": "error.badUrl",
  "bad-peer-key": "error.badPeerKey",
  "bad-mark": "error.badMark",
  "bad-table": "error.badTable",
  "bad-import": "error.badImport",
  "no-mark": "error.noMark",
  "unknown-outbound": "error.unknownOutbound",
  "host-refused": "error.hostRefused",
  "bad-balancer-name": "error.badBalancerName",
  "bad-strategy": "error.badStrategy",
  "no-members": "error.noMembers",
  "too-many-members": "error.tooManyMembers",
  "same-member": "error.sameMember",
  "unknown-balancer": "error.unknownBalancer",
  "balancer-in-use": "error.balancerInUse",
  "balancer-off": "error.balancerOff",
  "no-live-member": "error.noLiveMember",
  "no-upstream": "error.noUpstream",
  "bad-upstream": "error.badUpstream",
  "bad-listen": "error.badListen",
  "bad-path": "error.badPath",
  "bad-domain": "error.badDomain",
  "bad-certificate": "error.badCertificate",
  "certificate-no-folder": "error.certificateNoFolder",
  "certificate-not-found": "error.certificateNotFound",
  "certificate-is-folder": "error.certificateIsFolder",
  "certificate-denied": "error.certificateDenied",
  "certificate-unreadable": "error.certificateUnreadable",
  "certificate-empty": "error.certificateEmpty",
  "certificate-invalid": "error.certificateInvalid",
  "certificate-key-no-folder": "error.certificateKeyNoFolder",
  "certificate-key-not-found": "error.certificateKeyNotFound",
  "certificate-key-is-folder": "error.certificateKeyIsFolder",
  "certificate-key-denied": "error.certificateKeyDenied",
  "certificate-key-unreadable": "error.certificateKeyUnreadable",
  "certificate-key-empty": "error.certificateKeyEmpty",
  "certificate-key-invalid": "error.certificateKeyInvalid",
  "certificate-key-encrypted": "error.certificateKeyEncrypted",
  "certificate-key-mismatch": "error.certificateKeyMismatch",
  "bad-language": "error.badLanguage",
  "bad-lifetime": "error.badLifetime",
  "bad-cache": "error.badCache",
  "bad-ttl": "error.badTtl",
  "bad-rule-name": "error.badRuleName",
  "bad-action": "error.badAction",
  "bad-target": "error.badTarget",
  "bad-source": "error.badSource",
  "bad-protocol": "error.badProtocol",
  "no-outbound": "error.noOutbound",
  "too-many": "error.tooMany",
  "unknown-rule": "error.unknownRule",
  "empty-target": "error.emptyTarget",
  "outbound-off": "error.outboundOff",
  "url-taken": "error.urlTaken",
  "unknown-source": "error.unknownSource",
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

export function reason(code: string): TextKey {
  return complaints[code] ?? "error.failed"
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
