import type { ProxyDraft, TransportProxy } from "@/api/proxies"
import type { Text } from "@/i18n"

export type ProxyField = "name" | "host" | "port" | "listenPort" | "path" | "transport" | "targets" | "certificate" | "serverName" | "pin"

export function proxyHost(value: string): boolean {
  const text = value.trim()
  if (text.length === 0 || text.length > 253 || /[\x00-\x20/\\%*@?#]/.test(text)) { return false }
  try {
    const bracketed = text.startsWith("[") && text.endsWith("]")
    if (!bracketed && /[[\]]/.test(text)) { return false }
    const url = new URL(`http://${text.includes(":") && !bracketed ? `[${text}]` : text}`)
    if (url.port || url.username || url.password) { return false }
    if (url.hostname.startsWith("[")) { return true }
    const labels = url.hostname.replace(/\.$/, "").split(".")
    return labels.every((one) => one.length > 0 && one.length <= 63 && /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/i.test(one))
  } catch { return false }
}

export function proxyFaults(t: Text, draft: ProxyDraft, id: number, all: TransportProxy[], path: string): Record<ProxyField, string> {
  const result: Record<ProxyField, string> = { name: "", host: "", port: "", listenPort: "", path, transport: "", targets: "", certificate: "", serverName: "", pin: "" }
  if (draft.name.trim().length === 0 || draft.name.trim().length > 64 || /[\x00-\x1f\x7f]/.test(draft.name)) { result.name = t("error.proxyName") }
  else if (all.some((one) => one.id !== id && protocol(one) === protocol(draft) && one.name.trim().toLowerCase() === draft.name.trim().toLowerCase())) { result.name = t("error.nameTaken") }
  if (!proxyHost(draft.host)) { result.host = t("error.proxyHost") }
  if (!Number.isInteger(draft.port) || draft.port < 1 || draft.port > 65535) { result.port = t("error.badPort") }
  if (!Number.isInteger(draft.listenPort) || draft.listenPort < 0 || draft.listenPort > 65535) { result.listenPort = t("error.badPort") }
  if (draft.path.toLowerCase() === "api") { result.path = t("error.badWebSocketPath") }
  if (draft.serverName.length > 0 && !proxyHost(draft.serverName)) { result.serverName = t("error.proxyHost") }
  if (draft.pin.length > 0 && !/^[a-f0-9]{64}$/i.test(draft.pin)) { result.pin = t("error.proxyPin") }
  const targets = draft.targets
  if (targets && (targets.destinations.length > 64 || targets.local && (!targets.restricted || targets.destinations.length > 0)
    || targets.restricted && !targets.local && targets.destinations.length === 0
    || targets.destinations.some((one) => !proxyHost(one.host) || !Number.isInteger(one.port) || one.port < 1 || one.port > 65535))) {
    result.targets = t("error.proxyTargets")
  }
  if (draft.useCertificate !== true || draft.originHttp) { result.certificate = t("error.proxyTlsRequired") }
  if (draft.transport === "xhttp" || draft.transport === "http" && (draft.protocolVersion !== 2 || !["GET", "POST"].includes(draft.httpMethod))) { result.transport = t("error.proxyTransport") }
  return result
}

function protocol(proxy: ProxyDraft): string {
  return proxy.transport === "xhttp" ? "http" : proxy.transport
}
