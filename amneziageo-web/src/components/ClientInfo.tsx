import { useState } from "react"
import type { ReactNode } from "react"
import { Link } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useClientInfo, useClientStatistics } from "@/api/clientInfo"
import type { ClientInformation } from "@/api/clientInfo"
import { useFailure } from "@/api/failure"
import { scopes } from "@/api/scopes"
import { card, fieldBox, label } from "@/components/styles"
import { bytes, rate, span } from "@/format"
import { useLanguage, useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

const sourceWords: Record<string, TextKey> = {
  client: "clientInfo.sourceClient",
  template: "clientInfo.sourceTemplate",
  interface: "clientInfo.sourceInterface",
  server: "clientInfo.sourceServer",
  default: "clientInfo.sourceDefault",
  "stand-ins": "clientInfo.sourceStandins",
  none: "clientInfo.none",
}

const reasonWords: Record<string, TextKey> = {
  "disabled-client": "clientInfo.disabledClient",
  "disabled-interface": "clientInfo.disabledInterface",
  "quota-blocked": "trafficLimits.quotaBlocked",
  "quota-throttled": "trafficLimits.throttled",
  "peer-not-present": "clients.notOnHost",
  "limits-not-applied": "trafficLimits.notApplied",
  "limits-blocked": "trafficLimits.blocked",
}

export function ClientInfo({ id }: { id: number }) {
  const t = useText()
  const language = useLanguage()
  const user = useAppSelector((s) => s.auth.user)
  const [days, setDays] = useState(30)
  const query = useClientInfo(id, days)
  const failed = useFailure(query)
  const data = query.data
  const date = (value: string | null) => value === null ? t("clientInfo.unavailable") : new Date(value).toLocaleString(language)
  const amount = (value: number | null) => value === null ? t("clientInfo.unavailable") : bytes(t, value)
  const speed = (value: number | null) => value === null ? t("clientInfo.unavailable") : rate(t, value)
  const source = (value: string) => t(sourceWords[value] ?? "clientInfo.unavailable")
  const yes = (value: boolean) => t(value ? "action.yes" : "action.no")

  if (!data) {
    return <div className="mt-4 text-sm text-muted" role={failed ? "alert" : undefined}>{failed ? t(complaint(failed)) : t("page.loading")}</div>
  }

  const connection = data.connection
  const traffic = data.traffic
  const config = data.configuration
  const reason = connection.reason === null ? null : (reasonWords[connection.reason] ? t(reasonWords[connection.reason]) : connection.reason)

  return (
    <div className="mt-4 flex min-w-0 flex-col gap-4" data-testid="client-info">
      {failed !== null && <div role="alert" className="text-sm text-alarm">{t(complaint(failed))}</div>}
      <Section title={t("clientInfo.connection")} name="connection">
        <Facts>
          <Fact title={t("clientInfo.enabled")}>{yes(data.isEnabled)}</Fact>
          <Fact title={t("clientInfo.state")}><span className={connection.isOnline ? "text-good" : "text-muted"}>{t(connection.isOnline ? "clientInfo.online" : "clientInfo.offline")}</span></Fact>
          <Fact title={t("clientInfo.peer")}>{yes(connection.isPresent)}</Fact>
          <Fact title={t("clientInfo.lastHandshake")}>{connection.lastHandshake === null ? t(connection.isPresent ? "clients.noHandshake" : "clientInfo.unavailable") : date(connection.lastHandshake)}</Fact>
          <Fact title={t("clientInfo.lastActivity")}>{date(connection.lastActivity)}</Fact>
          <Fact title={t("clientInfo.connectedSince")}>{date(connection.sessionStartedUtc)}</Fact>
          <Fact title={t("clientInfo.remoteEndpoint")}>{connection.endpoint ?? t("clientInfo.unavailable")}</Fact>
          {reason !== null && <Fact title={t("clientInfo.reason")}><span className="text-warn">{reason}</span></Fact>}
          {connection.cut.length > 0 && <Fact title={t("clientInfo.cut")}>{connection.cut.join(", ")}</Fact>}
        </Facts>
      </Section>

      <Section title={t("clients.traffic")} name="traffic" tools={
        <select aria-label={t("clientInfo.trafficPeriod")} value={days} onChange={(event) => setDays(Number(event.target.value))} className={`max-w-full ${fieldBox}`}>
          {[7, 30, 90].map((value) => <option key={value} value={value}>{t("clientInfo.lastDays", { days: value })}</option>)}
        </select>
      }>
        <Facts>
          <Fact title={t("clientInfo.peerFromClient")}>{amount(traffic.peerRxBytes)}</Fact>
          <Fact title={t("clientInfo.peerToClient")}>{amount(traffic.peerTxBytes)}</Fact>
          <Fact title={t("clientInfo.speedFromClient")}>{speed(traffic.rxRate)}</Fact>
          <Fact title={t("clientInfo.speedToClient")}>{speed(traffic.txRate)}</Fact>
          <Fact title={t("clientInfo.todayFromClient")}>{amount(traffic.todayRx)}</Fact>
          <Fact title={t("clientInfo.todayToClient")}>{amount(traffic.todayTx)}</Fact>
          <Fact title={t("clientInfo.accountingDay")}>{traffic.day} · {traffic.timeZone}</Fact>
        </Facts>
        <h3 className="mt-5 text-xs font-medium text-muted">{t("clientInfo.dailyHistory")}</h3>
        {traffic.history.length === 0 ? <Empty>{t("clientInfo.noHistory")}</Empty> : (
          <div className="mt-2 max-h-60 overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-surface text-xs text-muted"><tr><th className="py-2 pr-3 font-normal">{t("clientInfo.day")}</th><th className="px-2 py-2 font-normal">{t("clients.fromClient")}</th><th className="py-2 pl-2 font-normal">{t("clients.toClient")}</th></tr></thead>
              <tbody>{traffic.history.map((one) => <tr key={one.day} className="border-t border-line"><td className="py-2 pr-3 whitespace-nowrap">{one.day}</td><td className="px-2 py-2">{bytes(t, one.rxBytes)}</td><td className="py-2 pl-2">{bytes(t, one.txBytes)}</td></tr>)}</tbody>
            </table>
          </div>
        )}
      </Section>

      <Limits limits={data.limits} />

      <Section title={t("clientInfo.configuration")} name="configuration">
        <Facts>
          <Fact title={t("clients.name")}>{data.name}</Fact>
          <Fact title={t("clientInfo.created")}>{date(data.createdUtc)}</Fact>
          <Fact title={t("clientInfo.updated")}>{date(data.updatedUtc)}</Fact>
          {config && <>
            <Fact title={t("clients.endpointName")}>{holds(user, scopes.manageInterfaces) ? <Link to={`/interfaces/${config.id}/edit`} className="text-brand-ink hover:text-brand-lit">{config.name}</Link> : config.name}</Fact>
            <Fact title={t("clientInfo.interfaceEnabled")}>{yes(config.isEnabled)}</Fact>
            <Fact title={t("clients.address")}>{config.address.length ? config.address.join(", ") : t("clientInfo.none")}</Fact>
            <Fact title={t("clients.template")}>{config.templateId === null ? (holds(user, scopes.manageClients) ? <Link to="/clients/templates/default" className="text-brand-ink hover:text-brand-lit">{t("clients.noTemplate")}</Link> : t("clients.noTemplate")) : holds(user, scopes.manageClients) ? <Link to={`/clients/templates/${config.templateId}/edit`} className="text-brand-ink hover:text-brand-lit">{config.templateName ?? `#${config.templateId}`}</Link> : (config.templateName ?? `#${config.templateId}`)}</Fact>
            <Fact title="Endpoint" origin={source("interface")}>{config.endpoint || t("clientInfo.unavailable")}</Fact>
            <Fact title="DNS" origin={source(config.dnsSource)}>{config.dns.length ? config.dns.join(", ") : t(config.dnsMode === "network" ? "clientInfo.networkDns" : "clientInfo.none")}</Fact>
            <Fact title="AllowedIPs" origin={source(config.allowedIpsSource)}>{config.allowedIps.length ? <div className="max-h-32 overflow-y-auto">{config.allowedIps.join(", ")}</div> : t("clientInfo.none")}{config.allowedIpsTruncated && <div className="mt-1 text-xs text-muted">{t("clientInfo.shownAddresses", { shown: config.allowedIps.length, total: config.allowedIpsTotal })}</div>}</Fact>
            <Fact title="MTU" origin={source(config.mtuSource)}>{config.mtu}</Fact>
            <Fact title="PersistentKeepalive" origin={source(config.keepaliveSource)}>{config.keepalive === 0 ? t("clientInfo.disabled") : span(t, config.keepalive)}</Fact>
            <Fact title={t("clients.inbound")}>{t(config.inbound === "server" || config.inbound === "host" ? "clients.inboundServer" : config.inbound === "network" ? "clients.inboundNetwork" : config.inbound === "off" ? "clients.inboundOff" : "clients.inboundEndpoint")}</Fact>
            <Fact title={t("clientInfo.presets")}>{config.presets.length ? config.presets.map((one) => holds(user, scopes.manageClients) ? <Link key={one.id} to={`/clients/presets/${one.id}/edit`} className="mr-2 inline-block text-brand-ink hover:text-brand-lit">{one.name}</Link> : <span className="mr-2 inline-block" key={one.id}>{one.name}</span>) : t("clientInfo.none")}</Fact>
            <Fact title={t("clientInfo.presetsOnly")}>{yes(config.presetsOnly)}</Fact>
            <Fact title={t("clientInfo.availableProxies")}>{config.proxyIds.length.toLocaleString(language)}</Fact>
            <Fact title={t("clientInfo.updateRequired")}>{yes(config.requireUpdate)}</Fact>
          </>}
        </Facts>
        {!config && <Empty>{t("clientInfo.configUnavailable")}</Empty>}
      </Section>

      <Statistics id={id} state={data.statistics} />
    </div>
  )
}

function Limits({ limits }: { limits: ClientInformation["limits"] }) {
  const t = useText()
  const language = useLanguage()
  const state = limits.status
  const policy = limits.policy
  const speed = (value: number) => value === 0 ? t("clients.noLimit") : new Intl.NumberFormat(language, { maximumFractionDigits: 6 }).format(value / 1_000_000)
  const date = (value: string) => `${new Date(value).toLocaleString(language, { timeZone: "UTC" })} UTC`
  const enabled = policy?.enabled === true

  return (
    <Section title={t("trafficLimits.current")} name="limits">
      <Facts>
        <Fact title={t("clientInfo.source")}>{t(sourceWords[limits.source])}</Fact>
        <Fact title={t("clientInfo.limitsState")}>{!enabled && limits.legacyDailyLimit === 0 ? t("clientInfo.noLimits") : state === null ? t("clientInfo.unavailable") : state.isBlocked ? t(state.isExhausted ? "trafficLimits.quotaBlocked" : "trafficLimits.blocked") : state.isThrottled ? t("trafficLimits.throttled") : !state.applied ? t("trafficLimits.notApplied") : t("clientInfo.active")}</Fact>
        {enabled && <>
          <Fact title={t("trafficLimits.download")}>{state ? speed(state.downloadBitsPerSecond) : t("clientInfo.unavailable")}</Fact>
          <Fact title={t("trafficLimits.upload")}>{state ? speed(state.uploadBitsPerSecond) : t("clientInfo.unavailable")}</Fact>
          <Fact title={t("clientInfo.applied")}>{state ? t(state.applied ? "action.yes" : "action.no") : t("clientInfo.unavailable")}</Fact>
          <Fact title={t("clientInfo.trafficQuota")}>{policy.quotaBytes > 0 ? bytes(t, policy.quotaBytes) : t("clients.noLimit")}</Fact>
          {policy.quotaBytes > 0 && <>
            <Fact title={t("trafficLimits.period")}>{t(`trafficLimits.${policy.period}`)}</Fact>
            <Fact title={t("trafficLimits.used")}>{state ? bytes(t, state.usedBytes) : t("clientInfo.unavailable")}</Fact>
            <Fact title={t("trafficLimits.remaining")}>{state?.remainingBytes == null ? t("clientInfo.unavailable") : bytes(t, state.remainingBytes)}</Fact>
            <Fact title={t("clientInfo.periodStart")}>{state ? date(state.startedUtc) : t("clientInfo.unavailable")}</Fact>
            <Fact title={t("trafficLimits.nextReset")}>{state ? date(state.endsUtc) : t("clientInfo.unavailable")}</Fact>
            <Fact title={t("trafficLimits.quotaAction")}>{t(policy.quotaAction === "block" ? "trafficLimits.block" : "trafficLimits.throttle")}</Fact>
          </>}
        </>}
        {!enabled && limits.legacyDailyLimit > 0 && <Fact title={t("clientInfo.legacyDailyLimit")}>{bytes(t, limits.legacyDailyLimit)}</Fact>}
      </Facts>
      {state && !state.applied && <div className="mt-3 text-sm text-warn">{t("trafficLimits.notApplied")}</div>}
      {state?.error && <div className="mt-2 break-words text-sm text-alarm">{state.error}</div>}
    </Section>
  )
}

function Statistics({ id, state }: { id: number; state: ClientInformation["statistics"] }) {
  const t = useText()
  const language = useLanguage()
  const [hours, setHours] = useState(1)
  const [since, setSince] = useState("")
  const [until, setUntil] = useState("")
  const from = new Date(since).getTime()
  const to = new Date(until).getTime()
  const custom = hours === 0
  const valid = !custom || (Number.isFinite(from) && Number.isFinite(to) && from < to && to - from <= 90 * 86400000)
  const range = custom && valid ? { from: new Date(from).toISOString(), to: new Date(to).toISOString() } : undefined
  const query = useClientStatistics(id, hours, state.canRead && valid, range)
  const data = query.data
  const summary = data?.summary
  const enabled = data?.isEnabled ?? state.isEnabled
  const running = data?.isRunning ?? state.isRunning
  const fault = data?.fault ?? state.fault
  const words: Record<string, TextKey> = { out: "access.channels", host: "access.direct", block: "access.block", held: "access.held", guard: "access.guard" }

  function period(value: number) {
    if (value === 0 && since === "" && until === "") {
      const now = new Date()
      setSince(local(new Date(now.getTime() - 3600000)))
      setUntil(local(now))
    }
    setHours(value)
  }

  return (
    <Section title={t("clientInfo.statistics")} name="statistics" tools={state.canRead ?
      <select aria-label={t("access.period")} value={hours} onChange={(event) => period(Number(event.target.value))} className={`max-w-full ${fieldBox}`}>
        <option value={1}>{t("access.hour")}</option><option value={6}>{t("access.hours6")}</option><option value={24}>{t("access.day")}</option><option value={168}>{t("access.week")}</option><option value={0}>{t("access.range")}</option>
      </select> : undefined
    }>
      {!state.canRead ? <Empty>{t("access.denied")}</Empty> : <>
        {custom && <div className="mb-4 grid min-w-0 grid-cols-1 gap-3 sm:grid-cols-2 xl:max-w-xl">
          <div className="min-w-0"><label className={label} htmlFor="client-statistics-from">{t("access.from")}</label><input id="client-statistics-from" type="datetime-local" value={since} onChange={(event) => setSince(event.target.value)} aria-invalid={!valid} className={`mt-1 w-full min-w-0 ${fieldBox}`} /></div>
          <div className="min-w-0"><label className={label} htmlFor="client-statistics-to">{t("access.to")}</label><input id="client-statistics-to" type="datetime-local" value={until} onChange={(event) => setUntil(event.target.value)} aria-invalid={!valid} className={`mt-1 w-full min-w-0 ${fieldBox}`} /></div>
          {!valid && <div role="alert" className="text-sm text-alarm sm:col-span-2">{t("clientInfo.invalidPeriod")}</div>}
        </div>}
        <Facts>
          <Fact title={t("clientInfo.collection")}>{t(enabled === null ? "clientInfo.unavailable" : enabled ? "clientInfo.enabled" : "clientInfo.disabled")}</Fact>
          <Fact title={t("clientInfo.collector")}>{t(running === null ? "clientInfo.unavailable" : running ? "clientInfo.running" : "clientInfo.stopped")}</Fact>
          {data && <Fact title={t("access.period")}>{new Date(data.from).toLocaleString(language)} — {new Date(data.to).toLocaleString(language)}</Fact>}
          {data && Date.parse(data.bindingSince) > Date.parse(data.from) && <Fact title={t("clientInfo.dataSince")} hint={t("clientInfo.dataSinceHint")}>{new Date(data.bindingSince).toLocaleString(language)}</Fact>}
          {summary && summary.total > 0 && <Fact title={t("access.count")}>{summary.total.toLocaleString(language)}</Fact>}
        </Facts>
        {fault && <div role="alert" className="mt-3 break-words text-sm text-alarm">{fault}</div>}
        {query.error && <div role="alert" className="mt-3 text-sm text-alarm">{t(complaint(query.error))}</div>}
        {query.isPending && valid && <Empty>{t("page.loading")}</Empty>}
        {summary?.total === 0 && <Empty>{t("clientInfo.noHistory")}</Empty>}
        {summary && summary.total > 0 && <>
          <dl className="mt-4 flex flex-wrap gap-x-6 gap-y-3">{summary.verdicts.map((one) => <div key={one.key}><dt className="text-xs text-muted">{words[one.key] ? t(words[one.key]) : one.key}</dt><dd className="mt-1 text-sm">{one.count.toLocaleString(language)}</dd></div>)}</dl>
          {summary.groups.length > 0 && <div className="mt-4 max-h-60 overflow-auto"><table className="w-full text-left text-sm"><thead className="sticky top-0 bg-surface text-xs text-muted"><tr><th className="py-2 pr-3 font-normal">{t("access.domain")}</th><th className="px-2 py-2 font-normal">{t("access.count")}</th><th className="py-2 pl-2 font-normal">{t("access.way")}</th></tr></thead><tbody>{summary.groups.map((one, index) => <tr key={`${one.key}-${index}`} className="border-t border-line"><td className="max-w-32 py-2 pr-3 break-words">{one.key || t("clientInfo.none")}</td><td className="px-2 py-2">{one.count.toLocaleString(language)}</td><td className="max-w-32 py-2 pl-2 break-words">{one.way || (words[one.verdict] ? t(words[one.verdict]) : one.verdict)}</td></tr>)}</tbody></table></div>}
          {summary.isCut && <div className="mt-2 text-xs text-muted">{t("access.cut", { count: summary.groups.length })}</div>}
        </>}
      </>}
    </Section>
  )
}

function Section({ title, name, tools, children }: { title: string; name: string; tools?: ReactNode; children: ReactNode }) {
  return <section className={`min-w-0 p-4.5 ${card}`} data-testid={`client-info-${name}`}><div className="flex flex-wrap items-center justify-between gap-3"><h2 className="text-sm font-semibold text-ink-soft">{title}</h2>{tools}</div><div className="mt-4">{children}</div></section>
}

function Facts({ children }: { children: ReactNode }) {
  return <dl className="grid min-w-0 grid-cols-1 gap-x-6 gap-y-4 sm:grid-cols-2 xl:grid-cols-3">{children}</dl>
}

function Fact({ title, origin, hint, children }: { title: string; origin?: string; hint?: string; children: ReactNode }) {
  return <div className="min-w-0" title={hint}><dt className="text-xs text-muted">{title}</dt><dd className="mt-1 break-words text-sm text-ink">{children}</dd>{origin && <div className="mt-1 text-xs text-muted">{origin}</div>}</div>
}

function Empty({ children }: { children: ReactNode }) {
  return <div className="mt-3 text-sm text-muted">{children}</div>
}

function local(at: Date): string {
  return new Date(at.getTime() - at.getTimezoneOffset() * 60000).toISOString().slice(0, 16)
}
