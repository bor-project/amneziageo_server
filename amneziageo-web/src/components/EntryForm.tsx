import { useState } from "react"
import { complaint } from "@/api/auth"
import { useBalancers } from "@/api/balancers"
import { useClients } from "@/api/clients"
import { useConfigs } from "@/api/configs"
import { useOutbounds } from "@/api/outbounds"
import type { EntryCriterion, EntryDraft, RuleProtocol } from "@/api/routingEntries"
import { useTrafficRules } from "@/api/trafficRules"
import { dnsChoice, dnsChosen, dnsPresets } from "@/components/dnsPolicy"
import type { DnsChoice } from "@/components/dnsPolicy"
import { useEditorExit } from "@/components/EditorExit"
import { EffectiveDns } from "@/components/EffectiveDns"
import { Line, Links, ListArea, ListLine, Multi, Part, Pick, Switch } from "@/components/fields"
import { Move } from "@/components/Move"
import { clientTo, configTo, wayTo } from "@/components/members"
import { danger, footer, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { same } from "@/store/draftSlice"

type Way = "direct" | "block" | "server" | "balancer"

export function EntryForm({ start, resume, isDefault = false, pending, error, onSave, onClose, onRemove, onAddresses, onHandling }: {
  start: EntryDraft
  resume?: EntryDraft
  isDefault?: boolean
  pending: boolean
  error: unknown
  onSave: (draft: EntryDraft) => Promise<unknown>
  onClose: () => void
  onRemove?: () => void
  onAddresses: (draft: EntryDraft, id?: number) => void
  onHandling: (draft: EntryDraft, path: string) => void
}) {
  const t = useText()
  const rules = useTrafficRules().data ?? []
  const outbounds = useOutbounds().data ?? []
  const balancers = useBalancers().data ?? []
  const clients = useClients().data ?? []
  const configs = useConfigs().data ?? []
  const [draft, setDraft] = useState(resume ?? start)
  const [pickedWay, setPickedWay] = useState<Way>("server")
  const [any, setAny] = useState(() => (resume !== undefined || start.name.length > 0) && (resume ?? start).criterion?.targets.length === 0)
  const criterion = draft.criterion
  const fresh = start.name.length === 0
  const form = criterion === undefined || !any ? draft : { ...draft, criterion: { ...criterion, targets: [] } }
  const edited = !same(form, start)
  const { leave, prompt } = useEditorExit(edited, pending)
  const storedWildcard = !fresh && start.criterion?.targets.length === 0 && !hasScope(start)
  const way = draft.action !== "out" ? draft.action
    : draft.outbound.length === 0 ? pickedWay
    : balancers.some((one) => one.name === draft.outbound) ? "balancer" : "server"
  const next = draft.action === "out" ? wayTo(draft.outbound, outbounds, balancers) : undefined
  const addressNames = draft.ruleIds.map((id) => rules.find((one) => one.id === id)?.name ?? String(id))
  const selected = draft.ruleIds.flatMap((id) => rules.filter((one) => one.id === id))
  const skipRequired = fresh && draft.action === "block" && transportScoped(draft)
  const scoped = [draft.clients.join(", "), draft.inbounds.join(", "), draft.ports.join(", "),
    draft.sourcePorts.join(", "), draft.protocol === "any" ? "" : draft.protocol.toUpperCase()].filter(Boolean).join(" · ")

  function put(change: Partial<EntryDraft>) {
    const next = { ...draft, ...change }
    if (next.criterion !== undefined && fresh && next.action === "block" && transportScoped(next)) {
      next.criterion = { ...next.criterion, dnsMode: "skip", dns: [], dnsMembers: {} }
    }
    setDraft(next)
  }

  function moveAddress(at: number, step: number) {
    const ruleIds = [...draft.ruleIds]
    ;[ruleIds[at], ruleIds[at + step]] = [ruleIds[at + step], ruleIds[at]]
    put({ ruleIds })
  }

  function chooseWay(value: Way) {
    if (value === "direct" || value === "block") {
      const retained = value === "direct" && criterion?.dnsMode === "skip"
        ? { ...criterion, dnsMode: "default" as const, dns: [], dnsMembers: {} } : criterion
      put({ action: value, outbound: "", criterion: retained })
      return
    }

    setPickedWay(value)
    const retained = criterion?.dnsMode === "skip" ? { ...criterion, dnsMode: "default" as const, dns: [], dnsMembers: {} } : criterion
    put({ action: "out", outbound: value === way && draft.action === "out" ? draft.outbound : "", criterion: retained })
  }

  const name = draft.name.trim() || addressNames[0] || draft.outbound || t("rules.actionDirect")
  const hasTarget = isDefault || (criterion === undefined ? draft.ruleIds.length > 0 : any
    ? hasScope(draft) || storedWildcard
    : criterion.targets.length > 0)
  const hasDns = criterion?.dnsMode !== "custom" || draft.action === "block" || criterion.dns.length > 0
    || Object.values(criterion.dnsMembers).some((one) => one.length > 0)
  const maySave = edited && !pending && hasTarget && hasDns && (draft.action !== "out" || draft.outbound.length > 0)
  const destinations = way === "balancer" ? balancers : outbounds.filter((one) => one.kind !== "local")
  const orphan = draft.outbound !== "" && !destinations.some((one) => one.name === draft.outbound)
  return <div className="mt-4 flex flex-col gap-4">
    {!isDefault && <Part title={t("routing.ruleName")}>
      <Line id="entry-name" caption={t("rules.name")} value={draft.name} onChange={(name) => put({ name })} />
      <Switch id="entry-enabled" caption={t("rules.enabled")} value={draft.isEnabled} onChange={(isEnabled) => put({ isEnabled })} />
    </Part>}

    {!isDefault && <Part title={t("routing.destination")}>
      {criterion !== undefined ? <>
        <Pick id="entry-destination" caption={t("routing.destination")} value={any ? "any" : "selected"} onChange={(value) => setAny(value === "any")}>
          <option value="selected">{t("routing.selectedAddresses")}</option>
          <option value="any">{t("routing.anyAddress")}</option>
        </Pick>
        {!any && <div className="sm:col-span-2"><ListArea id="entry-targets" caption={t("rules.targets")} value={criterion.targets}
          onChange={(targets) => put({ criterion: { ...criterion, targets } })} rows={4} /></div>}
      </> : <>
        <div className="sm:col-span-2">
          <div className="mb-2 flex items-center justify-between gap-3"><span className={label}>{t("routing.addresses")}</span>
            <button type="button" className="text-sm text-brand-ink" onClick={() => leave(() => onAddresses(draft))}>{t("routing.createAddressSet")}</button></div>
          <Links id="entry-rules" value={draft.ruleIds.map(String)}
            items={rules.map((one) => ({ key: String(one.id), name: one.name }))}
            placeholder={t("rules.pick")} onChange={(ids) => put({ ruleIds: ids.map(Number) })} />
          {selected.length > 0 && <div className="mt-4 border-t border-line">{selected.map((one, at) =>
            <div key={one.id} className="grid gap-2 border-b border-line py-3 text-sm last:border-b-0 sm:grid-cols-2">
              <div className="flex items-center justify-between gap-3">
                <button type="button" className="text-left text-brand-ink" onClick={() => leave(() => onAddresses(draft, one.id))}>{one.name}</button>
                {selected.length > 1 && <span className="flex shrink-0"><Move first={at === 0} last={at === selected.length - 1}
                  upTitle={t("rules.up")} downTitle={t("rules.down")} onMove={(up) => moveAddress(at, up ? -1 : 1)} /></span>}
              </div>
              <EffectiveDns rule={one} entry={draft} />
            </div>)}</div>}
        </div>
      </>}
    </Part>}

    {!isDefault && <details className="rounded-xl border border-line bg-surface p-4.5">
      <summary className="w-fit cursor-pointer text-sm font-semibold text-ink">
        {t("routing.conditions")} <span className="ml-1 font-normal text-muted">{scoped || t("routing.all")}</span>
      </summary>
      <div className="mt-4 grid gap-3.5 sm:grid-cols-2">
        <div>
          <span className={label}>{t("rules.clients")}</span>
          <div className="mt-1"><Multi id="entry-clients" value={draft.clients} offers={clients.map((one) => one.name)} placeholder={t("rules.anyone")}
            linkOf={(name) => clientTo(name, clients)} onChange={(value) => put({ clients: value })} /></div>
        </div>
        <div>
          <span className={label}>{t("rules.interfaces")}</span>
          <div className="mt-1"><Multi id="entry-inbounds" value={draft.inbounds} offers={configs.map((one) => one.name)} placeholder={t("rules.anyInbound")}
            linkOf={(name) => configTo(name, configs)} onChange={(value) => put({ inbounds: value })} /></div>
        </div>
        <Pick id="entry-protocol" caption={t("rules.protocol")} value={draft.protocol} onChange={(protocol) => put({ protocol: protocol as RuleProtocol })}>
          <option value="any">{t("rules.protocolAny")}</option><option value="tcp">TCP</option><option value="udp">UDP</option>
        </Pick>
        <ListLine id="entry-ports" caption={t("rules.ports")} value={draft.ports} onChange={(ports) => put({ ports })} />
        <ListLine id="entry-source-ports" caption={t("rules.sourcePorts")} value={draft.sourcePorts} onChange={(sourcePorts) => put({ sourcePorts })} />
      </div>
    </details>}

    <Part title={t("routing.to")}>
      <Pick id="entry-action" caption={t("routing.route")} value={way} onChange={(value) => chooseWay(value as Way)}>
        <option value="direct">{t("rules.actionDirect")}</option>
        <option value="block">{t("rules.actionBlock")}</option>
        <option value="server">{t("routing.viaServer")}</option>
        <option value="balancer">{t("routing.viaBalancer")}</option>
      </Pick>
      {draft.action === "out" && <div className="flex min-w-0 flex-wrap items-end gap-2">
        <div className="min-w-32 flex-1">
          <Pick id="entry-outbound" caption={t(way === "balancer" ? "routing.balancers" : "routing.linkedServer")} value={draft.outbound}
            onChange={(outbound) => put({ outbound })}>
            <option value="">{t("rules.pick")}</option>
            {destinations.map((one) => <option key={one.id} value={one.name}>{one.name}</option>)}
            {orphan && <option value={draft.outbound}>{draft.outbound}</option>}
          </Pick>
        </div>
        {(next !== undefined || destinations.length === 0) && <button type="button" className={secondary}
          onClick={() => leave(() => onHandling(form, next ?? (way === "balancer" ? "/routing/balancers/new" : "/routing/channels/new")))}>
          {t(next === undefined ? "action.add" : "action.goTo")}
        </button>}
      </div>}
    </Part>

    {criterion !== undefined && <CriterionDns criterion={criterion} entry={draft} isDefault={isDefault}
      skipRequired={skipRequired} onChange={(criterion) => put({ criterion })} />}
    {error != null && <div className="text-sm text-alarm">{t(complaint(error))}</div>}
    <div className={footer}>
      {!isDefault && onRemove && <button type="button" className={`mr-auto ${danger}`} onClick={onRemove}>{t("rules.remove")}</button>}
      <button type="button" className={secondary} onClick={onClose} disabled={pending}>{t("rules.cancel")}</button>
      <button type="button" className={primary} onClick={() => void onSave({ ...form, name }).then(() => leave(onClose)).catch(() => undefined)}
        disabled={!maySave}>{t(pending ? "rules.busy" : "rules.save")}</button>
    </div>
    {prompt}
  </div>
}

function hasScope(entry: EntryDraft): boolean {
  return entry.clients.length > 0 || entry.inbounds.length > 0 || transportScoped(entry)
}

function transportScoped(entry: EntryDraft): boolean {
  return entry.protocol !== "any" || entry.ports.length > 0 || entry.sourcePorts.length > 0
}

function CriterionDns({ criterion, entry, isDefault, skipRequired, onChange }: {
  criterion: EntryCriterion
  entry: EntryDraft
  isDefault: boolean
  skipRequired: boolean
  onChange: (criterion: EntryCriterion) => void
}) {
  const t = useText()
  const block = entry.action === "block"
  const legacy = { ...criterion, dnsMode: criterion.dnsMode === "skip" ? "default" as const : criterion.dnsMode }
  const choice = criterion.dnsMode === "skip" ? "skip" : block ? "default" : dnsChoice(legacy)

  function put(value: DnsChoice | "skip") {
    if (value === "skip") {
      onChange({ ...criterion, dnsMode: "skip", dns: [], dnsMembers: {} })
      return
    }

    onChange({ ...criterion, ...dnsChosen(legacy, value) })
  }

  return <div id="entry-dns"><Part title={t("routing.dnsHeading")}>
    <div className="sm:col-span-2">
      <Pick id="entry-dns-mode" caption={t("tab.dns")} value={choice} onChange={(value) => put(value as DnsChoice | "skip")}>
        {(!skipRequired || choice !== "skip") && <option value="default">{t(block ? "routing.dnsReject" : "routing.dnsRoute")}</option>}
        {(block && !isDefault || choice === "skip") && <option value="skip">{t("routing.dnsSkip")}</option>}
        {!block && dnsPresets.map((one) => <option key={one.key} value={one.key}>{one.name} · {one.servers.join(", ")}</option>)}
        {!block && <option value="custom">{t("routing.dnsCustom")}</option>}
      </Pick>
    </div>
    {!block && choice === "custom" && <div className="sm:col-span-2"><ListArea id="entry-dns-addresses" caption={t("routing.dnsAddresses")}
      value={criterion.dns} onChange={(dns) => onChange({ ...criterion, dns })} rows={3} /></div>}
    {!block && criterion.dnsMode === "custom" && Object.entries(criterion.dnsMembers).map(([name, servers]) =>
      <ListArea key={name} id={`entry-member-dns-${name}`} caption={name} value={servers}
        onChange={(dns) => onChange({ ...criterion, dnsMembers: { ...criterion.dnsMembers, [name]: dns } })} rows={3} />)}
    {!block && <div className="sm:col-span-2"><EffectiveDns rule={criterion} entry={entry} /></div>}
  </Part></div>
}
