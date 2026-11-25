import { useState } from "react"
import { Link } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useRoutingEntries } from "@/api/routingEntries"
import type { EntryReturn } from "@/components/routingReturn"
import type { TrafficRuleDraft } from "@/api/trafficRules"
import { dnsChoice, dnsChosen, dnsPresets } from "@/components/dnsPolicy"
import type { DnsChoice } from "@/components/dnsPolicy"
import { EffectiveDns } from "@/components/EffectiveDns"
import { Line, ListArea, Part, Pick } from "@/components/fields"
import { danger, footer, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { same } from "@/store/draftSlice"

export function RuleForm({ start, self, returning, pending, error, onSave, onClose, onRemove }: {
  start: TrafficRuleDraft
  self?: number
  returning?: EntryReturn
  pending: boolean
  error: unknown
  onSave: (draft: TrafficRuleDraft) => void
  onClose: () => void
  onRemove?: () => void
}) {
  const t = useText()
  const [draft, setDraft] = useState(start)
  const entries = useRoutingEntries()
  const returnedId = returning?.to.match(/\/rules\/(\d+)\/edit/)?.[1]
  const uses = (entries.data ?? []).filter((one) => one.ruleIds.includes(self ?? 0) && String(one.id) !== returnedId)
  const edited = !same(draft, start)
  const [choice, setChoice] = useState<DnsChoice>(dnsChoice(start))
  const hasDns = draft.dnsMode === "default" || draft.dns.length > 0 || Object.values(draft.dnsMembers).some((values) => values.length > 0)
  const hasTargets = draft.targets.length > 0 || self !== undefined && start.targets.length === 0

  function put(change: Partial<TrafficRuleDraft>) {
    setDraft({ ...draft, ...change })
  }

  return <div className="mt-4 flex flex-col gap-4">
    <Part title={t("routing.addressSet")}>
      <Line id="rule-name" caption={t("rules.name")} value={draft.name} onChange={(name) => put({ name })} wide />
      <div className="sm:col-span-2"><ListArea id="rule-targets" caption={t("rules.targets")} value={draft.targets} onChange={(targets) => put({ targets })} /></div>
    </Part>
    <Part title={t("tab.dns")}>
      <div className="sm:col-span-2"><Pick id="rule-dns-mode" caption={t("tab.dns")} value={choice} onChange={(value) => { setChoice(value as DnsChoice); put(dnsChosen(draft, value as DnsChoice)) }}>
        <option value="default">{t("routing.dnsDefaultFromHandling")}</option>
        {dnsPresets.map((one) => <option key={one.key} value={one.key}>{one.name} · {one.servers.join(", ")}</option>)}
        <option value="custom">{t("routing.dnsCustom")}</option>
      </Pick></div>
      {choice === "custom" && <div className="sm:col-span-2"><ListArea id="rule-dns" caption={t("routing.dnsAddresses")} value={draft.dns} onChange={(dns) => put({ dns })} /></div>}
      {draft.dnsMode === "custom" && Object.keys(draft.dnsMembers ?? {}).length > 0 && <div className="sm:col-span-2 grid gap-3.5 sm:grid-cols-2">
        {Object.entries(draft.dnsMembers).map(([name, servers]) => <ListArea key={name} id={`rule-member-dns-${name}`} caption={name}
          value={servers} onChange={(dns) => put({ dnsMembers: { ...draft.dnsMembers, [name]: dns } })} />)}
      </div>}
      <div className="sm:col-span-2 border-t border-line pt-4">
        <h3 className="mb-3 text-sm font-semibold">{t("routing.addressUsage")}</h3>
        {returning && <div className="grid gap-2 border-b border-line py-3 text-sm sm:grid-cols-2">
          <button type="button" onClick={onClose} className="text-left text-brand-ink">{returning.draft.name || t("routing.newEntry")}</button>
          <EffectiveDns rule={draft} entry={returning.draft} />
        </div>}
        {uses.map((one) => <div key={one.id} className="grid gap-2 border-b border-line py-3 text-sm last:border-b-0 sm:grid-cols-2">
          <Link to={`/routing/rules/${one.id}/edit`} className="text-brand-ink">{one.name}</Link>
          <EffectiveDns rule={draft} entry={one} />
        </div>)}
        {uses.length === 0 && returning === undefined && <div className="text-sm text-muted">{t("routing.addressUnused")}</div>}
      </div>
    </Part>
    {error != null && <div className="text-sm text-alarm">{t(complaint(error))}</div>}
    <div className={footer}>
      {onRemove && <button type="button" className={`mr-auto ${danger}`} onClick={onRemove}>{t("rules.remove")}</button>}
      <button type="button" className={secondary} onClick={onClose} disabled={pending}>{t("rules.cancel")}</button>
      <button type="button" className={primary} onClick={() => onSave(draft)} disabled={!edited || pending || draft.name.trim() === "" || !hasTargets || !hasDns}>
        {t(pending ? "rules.busy" : "rules.save")}
      </button>
    </div>
  </div>
}
