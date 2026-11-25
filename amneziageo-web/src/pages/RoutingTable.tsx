import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { complaint, reason } from "@/api/auth"
import { useBalancers } from "@/api/balancers"
import { useClients } from "@/api/clients"
import { useConfigs } from "@/api/configs"
import { findingsOf, useDnsConflicts } from "@/api/dns"
import { useFailure } from "@/api/failure"
import { useOutbounds } from "@/api/outbounds"
import { useMoveEntry, usePlaceEntry, useRoutingEntries, useSwitchEntry } from "@/api/routingEntries"
import type { RoutingEntry } from "@/api/routingEntries"
import { scopes } from "@/api/scopes"
import { useTrafficRules } from "@/api/trafficRules"
import { DnsChoiceLabel, EffectiveDns } from "@/components/EffectiveDns"
import { DnsMark } from "@/components/DnsMark"
import { Find, Knob, Pick } from "@/components/fields"
import { clientTo, configTo, wayTo } from "@/components/members"
import { Move } from "@/components/Move"
import { RoutingExchange } from "@/components/RoutingExchange"
import { RoutingSettings, RoutingToolbar } from "@/components/RoutingEditor"
import { Rows } from "@/components/Rows"
import { alarmLine, card } from "@/components/styles"
import { useText } from "@/i18n"
import { RouteTest } from "@/pages/RouteTest"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function RoutingTable() {
  const t = useText()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const entries = useRoutingEntries()
  const rules = useTrafficRules().data ?? []
  const failed = useFailure(entries)
  const outbounds = useOutbounds().data ?? []
  const balancers = useBalancers().data ?? []
  const clients = useClients().data ?? []
  const configs = useConfigs().data ?? []
  const report = useDnsConflicts().data
  const move = useMoveEntry()
  const place = usePlaceEntry()
  const toggle = useSwitchEntry()
  const may = holds(useAppSelector((state) => state.auth.user), scopes.manageRouting)
  const find = params.get("find") ?? ""
  const client = params.get("client") ?? ""
  const inbound = params.get("inbound") ?? ""
  const rule = params.get("rule") ?? ""
  const all = entries.data ?? []
  const number = new Map(all.map((one, at) => [one.id, at + 1]))
  const definitions = (one: RoutingEntry) => one.ruleIds.flatMap((id) => rules.filter((definition) => definition.id === id))
  const shown = all.filter((one) => (client === "" || one.clients.length === 0 || one.clients.some((name) => name.toLowerCase() === client.toLowerCase()))
    && (inbound === "" || one.inbounds.length === 0 || one.inbounds.includes(inbound))
    && (rule === "" || one.ruleIds.includes(Number(rule)))
    && [one.name, one.outbound, ...one.clients, ...one.inbounds, ...definitions(one).flatMap((definition) => [definition.name, ...definition.targets, ...definition.dns])]
      .some((text) => text.toLowerCase().includes(find.trim().toLowerCase())))

  function filter(key: string, value: string) {
    const next = new URLSearchParams(params)
    if (value === "") { next.delete(key) } else { next.set(key, value) }
    setParams(next, { replace: true })
  }

  return <>
    <RoutingToolbar onAdd={may && entries.isSuccess ? () => navigate("/routing/rules/new") : undefined}>
      <Pick id="table-client" caption={t("routing.client")} value={client} onChange={(value) => filter("client", value)}>
        <option value="">{t("routing.all")}</option>{clients.map((one) => <option key={one.id} value={one.name}>{one.name}</option>)}
      </Pick>
      <Pick id="table-inbound" caption={t("routing.inbound")} value={inbound} onChange={(value) => filter("inbound", value)}>
        <option value="">{t("routing.all")}</option>{configs.map((one) => <option key={one.id} value={one.name}>{one.name}</option>)}
      </Pick>
      <Pick id="table-rule" caption={t("routing.addresses")} value={rule} onChange={(value) => filter("rule", value)}>
        <option value="">{t("routing.all")}</option>{rules.map((one) => <option key={one.id} value={one.id}>{one.name}</option>)}
      </Pick>
      <Find value={find} onChange={(value) => filter("find", value)} className="w-full wide:max-w-64" />
    </RoutingToolbar>
    <div className={card}>
      {failed !== null && <div className={alarmLine}>{t(complaint(failed))}</div>}
      {entries.isPending && <div className="p-4 text-sm text-muted">{t("rules.loading")}</div>}
      {entries.data !== undefined && <Rows name="routing-entry-order" items={shown} keyOf={(one) => one.id}
        drag={may && find === "" && client === "" && inbound === "" && rule === "" ? { title: t("rules.drag"),
          onMove: (one, to) => void place.mutateAsync({ id: one.id, to: number.get(to.id) ?? 1 }).catch(() => undefined) } : undefined}
        columns={[
          { key: "place", caption: "№", width: 44, cell: (one) => number.get(one.id) },

          { key: "processing", caption: t("routing.processing"), width: 180, lead: true, cell: (one) => {
            const to = one.action === "out" ? wayTo(one.outbound, outbounds, balancers) : undefined
            const title = one.action === "out"
              ? outbounds.some((way) => way.name === one.outbound && way.kind === "local") ? `${t("rules.actionDirect")} (${one.outbound})` : one.outbound
              : t(one.action === "block" ? "rules.actionBlock" : "rules.actionDirect")
            return <div>
              <div className="font-semibold">{to ? <Link to={to} className="text-brand-ink">{title}</Link> : title}</div>
              <div className="mt-1 flex items-center gap-2 text-xs text-muted">
                {may ? <Link to={`/routing/rules/${one.id}/edit`} className="hover:text-brand-ink">{one.name}</Link> : one.name}
                <DnsMark findings={findingsOf(report, "rule", one.id)} view="rule" />
              </div>
              {!one.isEnabled && <div className="mt-1 text-xs text-muted">{t("rules.off")}</div>}
              {one.isEnabled && one.state.fault && <div className="mt-1 text-xs text-alarm">{t(reason(one.state.fault))}</div>}
            </div>
          } },
          { key: "rules", caption: t("routing.addresses"), width: 180, wrap: true, cell: (one) => <div className="flex flex-col gap-1">
            {one.ruleIds.map((id) => <Link key={id} to={`/routing/addresses/${id}/edit`} className="w-fit rounded bg-chip px-1.5 py-0.5 text-xs text-chip-ink">{rules.find((definition) => definition.id === id)?.name ?? String(id)}</Link>)}
          </div> },
          { key: "source", caption: t("routing.filters"), width: 180, cell: (one) => <div className="flex flex-col gap-1 text-xs">
            {one.clients.length > 0 && <div><span className="text-muted">{t("routing.client")}: </span><Names values={one.clients} to={(name) => clientTo(name, clients)} /></div>}
            {one.inbounds.length > 0 && <div><span className="text-muted">{t("routing.inbound")}: </span><Names values={one.inbounds} to={(name) => configTo(name, configs)} /></div>}
            {one.protocol !== "any" && <div>{one.protocol.toUpperCase()}</div>}
            {one.ports.length > 0 && <div><span className="text-muted">{t("rules.ports")}: </span>{one.ports.join(", ")}</div>}
            {one.sourcePorts.length > 0 && <div><span className="text-muted">{t("rules.sourcePorts")}: </span>{one.sourcePorts.join(", ")}</div>}
            {one.clients.length === 0 && one.inbounds.length === 0 && one.protocol === "any" && one.ports.length === 0 && one.sourcePorts.length === 0 && <span className="text-muted">{t("routing.all")}</span>}
          </div> },
          { key: "dns", caption: t("tab.dns"), width: 220, wrap: true, cell: (one) => <div className="flex flex-col gap-3 text-xs">
            {definitions(one).map((definition) => <div key={definition.id}>
              {one.ruleIds.length > 1 && <div className="mb-1 text-muted">{definition.name}</div>}
              <EffectiveDns rule={definition} entry={one} />
              <div className="mt-1 text-muted"><DnsChoiceLabel rule={definition} /></div>
            </div>)}
          </div> },
          { key: "actions", caption: t("rules.actions"), width: 150, tail: true, cell: (one) => may && <div className="flex items-center justify-end gap-1">
            <Knob title={t("rules.enabled")} value={one.isEnabled} disabled={toggle.isPending}
              onChange={(on) => void toggle.mutateAsync({ id: one.id, on }).catch(() => undefined)} />
            <Move first={number.get(one.id) === 1 || move.isPending || place.isPending} last={number.get(one.id) === all.length || move.isPending || place.isPending}
              upTitle={t("rules.up")} downTitle={t("rules.down")} onMove={(up) => void move.mutateAsync({ id: one.id, up }).catch(() => undefined)} />
            <RoutingSettings onClick={() => navigate(`/routing/rules/${one.id}/edit`)} />
          </div> },
        ]} />}
    </div>
    {(move.error ?? place.error ?? toggle.error) != null && <div className="mt-2 text-sm text-alarm">{t(complaint(move.error ?? place.error ?? toggle.error))}</div>}
    <details className="mt-5 border-t border-line pt-3"><summary className="w-fit cursor-pointer text-sm text-brand-ink">{t("tab.test")}</summary><RouteTest /></details>
    <RoutingExchange may={may} />
  </>
}


function Names({ values, to }: { values: string[]; to: (name: string) => string | undefined }) {
  return <>{values.map((name, index) => {
    const link = to(name)
    return <span key={name}>{index > 0 && ", "}{link ? <Link to={link} className="text-brand-ink">{name}</Link> : name}</span>
  })}</>
}
