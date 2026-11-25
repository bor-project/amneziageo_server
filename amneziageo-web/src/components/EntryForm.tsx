import { useState } from "react"
import { complaint } from "@/api/auth"
import { useBalancers } from "@/api/balancers"
import { useClients } from "@/api/clients"
import { useConfigs } from "@/api/configs"
import { useOutbounds } from "@/api/outbounds"
import type { EntryDraft, RuleAction, RuleProtocol } from "@/api/routingEntries"
import { useTrafficRules } from "@/api/trafficRules"
import { EffectiveDns } from "@/components/EffectiveDns"
import { Line, Links, ListLine, Multi, Part, Pick, Switch } from "@/components/fields"
import { Move } from "@/components/Move"
import { clientTo, configTo, wayTo } from "@/components/members"
import { danger, footer, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { same } from "@/store/draftSlice"

export function EntryForm({ start, resume, pending, error, onSave, onClose, onRemove, onAddresses, onHandling }: {
  start: EntryDraft
  resume?: EntryDraft
  pending: boolean
  error: unknown
  onSave: (draft: EntryDraft) => void
  onClose: () => void
  onRemove?: () => void
  onAddresses: (draft: EntryDraft, id?: number) => void
  onHandling: (draft: EntryDraft, path: string) => void
}) {
  const t = useText()
  const rules = useTrafficRules()
  const outbounds = useOutbounds()
  const balancers = useBalancers()
  const clients = useClients()
  const configs = useConfigs()
  const [draft, setDraft] = useState(resume ?? start)
  const edited = !same(draft, start)
  const way = draft.action === "out" ? wayTo(draft.outbound, outbounds.data ?? [], balancers.data ?? []) : undefined
  const addressNames = draft.ruleIds.map((id) => rules.data?.find((one) => one.id === id)?.name ?? String(id))
  const selected = draft.ruleIds.flatMap((id) => (rules.data ?? []).filter((one) => one.id === id))

  function put(change: Partial<EntryDraft>) {
    setDraft({ ...draft, ...change })
  }

  function moveAddress(at: number, step: number) {
    const ruleIds = [...draft.ruleIds]
    ;[ruleIds[at], ruleIds[at + step]] = [ruleIds[at + step], ruleIds[at]]
    put({ ruleIds })
  }

  return <div className="mt-4 flex flex-col gap-4">
    <Part title={t("routing.processing")}>
      <div className="sm:col-span-2"><Switch id="entry-enabled" caption={t("rules.enabled")} value={draft.isEnabled} onChange={(isEnabled) => put({ isEnabled })} /></div>
      <Line id="entry-name" caption={t("rules.name")} value={draft.name} onChange={(name) => put({ name })} />
      <Pick id="entry-action" caption={t("routing.processing")} value={draft.action === "out" ? "out:" + draft.outbound : draft.action}
        onChange={(value) => put(value.startsWith("out:") ? { action: "out", outbound: value.slice(4) } : { action: value as RuleAction, outbound: "" })}>
        <option value="direct">{t("rules.actionDirect")}</option>
        <option value="block">{t("rules.actionBlock")}</option>
        <optgroup label={t("routing.linkedServers")}>
          {(outbounds.data ?? []).filter((one) => one.kind !== "local").map((one) => <option key={one.id} value={`out:${one.name}`}>{one.name}</option>)}
        </optgroup>
        {(balancers.data ?? []).length > 0 && <optgroup label={t("routing.balancers")}>
          {(balancers.data ?? []).map((one) => <option key={one.id} value={`out:${one.name}`}>{one.name}</option>)}
        </optgroup>}
        {draft.action === "out" && way === undefined && <option value={`out:${draft.outbound}`}>{outbounds.data?.some((one) => one.name === draft.outbound && one.kind === "local") ? `${t("rules.actionDirect")} (${draft.outbound})` : draft.outbound || t("rules.pick")}</option>}
      </Pick>
      <div className="sm:col-span-2 border-t border-line pt-4">
        <div className="mb-2 flex items-center justify-between gap-3"><span className={label}>{t("routing.addresses")}</span>
          <button type="button" className="text-sm text-brand-ink" onClick={() => onAddresses(draft)}>{t("routing.createAddressSet")}</button></div>
        <Links id="entry-rules" value={draft.ruleIds.map(String)}
          items={(rules.data ?? []).map((one) => ({ key: String(one.id), name: one.name }))}
          placeholder={t("rules.pick")} onChange={(ids) => put({ ruleIds: ids.map(Number) })} />
        <div className="mt-4 border-t border-line">
          {selected.map((one, at) => <div key={one.id} className="grid gap-2 border-b border-line py-3 text-sm last:border-b-0 sm:grid-cols-2">
            <div className="flex items-center justify-between gap-3">
              <button type="button" className="text-left text-brand-ink" onClick={() => onAddresses(draft, one.id)}>{one.name}</button>
              {selected.length > 1 && <span className="flex shrink-0"><Move first={at === 0} last={at === selected.length - 1}
                upTitle={t("rules.up")} downTitle={t("rules.down")} onMove={(up) => moveAddress(at, up ? -1 : 1)} /></span>}
            </div>
            <EffectiveDns rule={one} entry={draft} />
          </div>)}
        </div>
        {way && <div className="mt-2 flex justify-end"><button type="button" onClick={() => onHandling(draft, way)} className="text-sm text-brand-ink">{t("action.goTo")}</button></div>}
      </div>
    </Part>
    <Part title={t("routing.filters")}>
      <div>
        <span className={label}>{t("rules.clients")}</span>
        <div className="mt-1"><Multi id="entry-clients" value={draft.clients} offers={(clients.data ?? []).map((one) => one.name)} placeholder={t("rules.anyone")}
          linkOf={(name) => clientTo(name, clients.data ?? [])} onChange={(value) => put({ clients: value })} /></div>
      </div>
      <div>
        <span className={label}>{t("rules.interfaces")}</span>
        <div className="mt-1"><Multi id="entry-inbounds" value={draft.inbounds} offers={(configs.data ?? []).map((one) => one.name)} placeholder={t("rules.anyInbound")}
          linkOf={(name) => configTo(name, configs.data ?? [])} onChange={(value) => put({ inbounds: value })} /></div>
      </div>
      <ListLine id="entry-ports" caption={t("rules.ports")} value={draft.ports} onChange={(ports) => put({ ports })} />
      <ListLine id="entry-source-ports" caption={t("rules.sourcePorts")} value={draft.sourcePorts} onChange={(sourcePorts) => put({ sourcePorts })} />
      <Pick id="entry-protocol" caption={t("rules.protocol")} value={draft.protocol} onChange={(protocol) => put({ protocol: protocol as RuleProtocol })}>
        <option value="any">{t("rules.protocolAny")}</option><option value="tcp">TCP</option><option value="udp">UDP</option>
      </Pick>
    </Part>
    {error != null && <div className="text-sm text-alarm">{t(complaint(error))}</div>}
    <div className={footer}>
      {onRemove && <button type="button" className={`mr-auto ${danger}`} onClick={onRemove}>{t("rules.remove")}</button>}
      <button type="button" className={secondary} onClick={onClose} disabled={pending}>{t("rules.cancel")}</button>
      <button type="button" className={primary} onClick={() => onSave({ ...draft, name: draft.name.trim() || addressNames[0] || draft.outbound || t("rules.actionDirect") })}
        disabled={!edited || pending || draft.ruleIds.length === 0 || draft.action === "out" && draft.outbound === ""}>
        {t(pending ? "rules.busy" : "rules.save")}
      </button>
    </div>
  </div>
}
