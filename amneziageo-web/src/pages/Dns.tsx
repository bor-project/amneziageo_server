import { useState } from "react"
import { Link } from "react-router-dom"
import { complaint } from "@/api/auth"
import { draftOf, findingsOf, useDnsCheck, useDnsConflicts, useResolver, useSaveResolver } from "@/api/dns"
import type { DnsDraft, DnsFinding, DnsReport, DnsState, DnsSubject, Resolver as ResolverSettings } from "@/api/dns"
import { useBalancers } from "@/api/balancers"
import { useConfigs } from "@/api/configs"
import { useFailure } from "@/api/failure"
import { useOutbounds } from "@/api/outbounds"
import { useRules } from "@/api/rules"
import { scopes } from "@/api/scopes"
import { AppCaption } from "@/components/AppMark"
import { DnsOutcome, DnsPick, DnsServers } from "@/components/DnsField"
import { DnsMark } from "@/components/DnsMark"
import { clientsOfWords, clientWords, counted, fewNames, lacksServers, levelOf, ruleWords, templateWords } from "@/components/dnsWords"
import { Box, Count, ListLine, Part, Pick, Shown } from "@/components/fields"
import { wayTo } from "@/components/members"
import { ResolverTrouble } from "@/components/ResolverTrouble"
import { troubleOf } from "@/components/trouble"
import { footer, primary, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { dnsDrafted, same } from "@/store/draftSlice"
import { useAppDispatch, useAppSelector } from "@/store/hooks"

export function Dns() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const resolver = useResolver()
  const failed = useFailure(resolver)
  const may = holds(user, scopes.manageRouting)

  return (
    <div className="mt-4 flex flex-col gap-4">
      {resolver.isPending && failed === null && <div className="text-sm text-muted">{t("page.loading")}</div>}
      {failed !== null && resolver.data === undefined && (
        <div className="text-sm text-alarm">{t(complaint(failed))}</div>
      )}
      {resolver.data && (
        <>
          <Watch state={resolver.data.state} />
          <Editor settings={resolver.data} may={may} />
        </>
      )}
    </div>
  )
}

function Watch({ state }: { state: DnsState }) {
  const t = useText()
  const language = useLanguage()

  return (
    <div className="flex flex-col gap-3.5">
      <div className="grid gap-3.5 [grid-template-columns:repeat(auto-fit,minmax(170px,1fr))]">
        <Box caption={t("dns.state")}>
          <div className={state.isRunning ? "text-[15px] text-good" : "text-[15px] text-muted"}>
            {t(state.isRunning ? "dns.running" : "dns.stopped")}
          </div>
        </Box>
        {state.listening.length > 0 && (
          <Box caption={t("dns.listening")}>
            <div className="text-[15px] text-body">{state.listening.join(", ")}</div>
          </Box>
        )}
        <Box caption={t("dns.questions")}>
          <div className="text-[15px] text-body">{state.questions.toLocaleString(language)}</div>
        </Box>
        <Box caption={t("dns.fromMemory")}>
          <div className="text-[15px] text-body">{state.cached.toLocaleString(language)}</div>
        </Box>
        <Box caption={t("dns.unanswered")}>
          <div className="text-[15px] text-body">{state.failed.toLocaleString(language)}</div>
        </Box>
        <Box caption={t("dns.added")}>
          <div className="text-[15px] text-body">{state.added.toLocaleString(language)}</div>
        </Box>
        <Box caption={t("dns.waiting")}>
          <div className="text-[15px] text-body">{state.waiting.toLocaleString(language)}</div>
        </Box>
        <Box caption={t("dns.names")}>
          <div className="text-[15px] text-body">{state.names.toLocaleString(language)}</div>
        </Box>
      </div>

      {state.fault.length > 0 && <div className="text-sm text-alarm">{state.fault}</div>}
    </div>
  )
}

function Editor({ settings, may }: { settings: ResolverSettings; may: boolean }) {
  const t = useText()
  const dispatch = useAppDispatch()
  const kept = useAppSelector((s) => s.drafts.dns)
  const [fault, setFault] = useState<TextKey | null>(null)
  const [way, setWay] = useState<"direct" | "out" | null>(null)
  const outbounds = useOutbounds()
  const balancers = useBalancers()
  const save = useSaveResolver()
  const saved = draftOf(settings)
  const draft = kept ?? saved
  const ways = [...(outbounds.data ?? []), ...(balancers.data ?? [])].map((one) => one.name)
  const via = way ?? (draft.outbound === "" ? "direct" : "out")
  const unpicked = via === "out" && draft.outbound === ""
  const trouble = troubleOf(settings)
  const report = useDnsConflicts()
  const check = useDnsCheck(kept === null ? null : { dns: draft })
  const findings = kept === null ? findingsOf(report.data, "dns", 0) : (check.data?.findings ?? [])
  const lines = useDnsCheck({ dns: draft }).data
  const files = lines === undefined ? undefined : { ...lines, findings: lines.findings.filter((one) => filed.includes(one.code)) }
  const endpoints = (useConfigs().data ?? [])
    .map((one) => `${one.name}: ${one.dns.length > 0 ? one.dns.join(", ") : t("dnsPick.endpointNone")}`)
    .join("; ")

  function set(part: Partial<DnsDraft>) {
    const next = { ...draft, ...part }
    dispatch(dnsDrafted(same(next, saved) ? null : next))
  }

  async function keep() {
    setFault(null)
    try {
      await save.mutateAsync(draft)
      dispatch(dnsDrafted(null))
      setWay(null)
    } catch (error) {
      setFault(complaint(error))
    }
  }

  function drop() {
    setFault(null)
    dispatch(dnsDrafted(null))
    setWay(null)
  }

  return (
    <>
      <Part title={<AppCaption caption={t("dns.serving")} what="resolver" />}>
        <div className="text-sm text-muted sm:col-span-2">{t("dns.about")}</div>
        <Pick
          id="dns-mode"
          caption={t("dns.mode")}
          value={draft.isEnabled ? "auto" : "off"}
          onChange={(v) => set({ isEnabled: v === "auto" })}
        >
          <option value="auto">{t("dns.modeAuto")}</option>
          <option value="off">{t("dns.modeOff")}</option>
        </Pick>
        <div className="flex items-end pb-2.5 text-sm">
          <Need
            findings={findings}
            report={report.data}
            state={settings.state}
            on={draft.isEnabled}
            known={report.isSuccess}
          />
        </div>
        {trouble?.kind === "kept" && (
          <div className="sm:col-span-2">
            <ResolverTrouble trouble={trouble} kept="dns.troubleKept" />
          </div>
        )}
        <Count id="dns-port" caption={t("dns.port")} value={draft.port} onChange={(v) => set({ port: v })} />
        <Count
          id="dns-life"
          caption={t("dns.nameMinutes")}
          value={draft.nameMinutes}
          onChange={(v) => set({ nameMinutes: v })}
        />
        <ListLine
          id="dns-listen"
          caption={t("dns.listen")}
          value={draft.listen}
          onChange={(listen) => set({ listen })}
          wide
        />
        <ListLine
          id="dns-upstreams"
          caption={t("dns.upstreams")}
          value={draft.upstreams}
          onChange={(upstreams) => set({ upstreams })}
          wide
        />
        <Pick
          id="dns-way"
          caption={t("dns.outbound")}
          value={via}
          onChange={(v) => {
            setWay(v === "out" ? "out" : null)
            if (v !== "out") {
              set({ outbound: "" })
            }
          }}
        >
          <option value="direct">{t("rules.actionDirect")}</option>
          <option value="out" disabled={ways.length === 0}>
            {t("rules.actionOut")}
          </option>
        </Pick>
        {via === "out" && (
          <div>
            <Pick
              id="dns-outbound"
              caption={t("rules.outbound")}
              value={draft.outbound}
              onChange={(v) => {
                setWay("out")
                set({ outbound: v })
              }}
            >
              <option value="">{t("rules.pick")}</option>
              {draft.outbound !== "" && !ways.includes(draft.outbound) && (
                <option value={draft.outbound}>{draft.outbound}</option>
              )}
              <optgroup label={t("rules.outbounds")}>
                {(outbounds.data ?? []).map((one) => (
                  <option key={one.id} value={one.name}>
                    {one.name}
                  </option>
                ))}
              </optgroup>
              {(balancers.data ?? []).length > 0 && (
                <optgroup label={t("rules.balancers")}>
                  {(balancers.data ?? []).map((one) => (
                    <option key={one.id} value={one.name}>
                      {one.name}
                    </option>
                  ))}
                </optgroup>
              )}
            </Pick>
            {wayTo(draft.outbound, outbounds.data ?? [], balancers.data ?? []) !== undefined && (
              <div className="mt-1 flex justify-end">
                <Link
                  to={wayTo(draft.outbound, outbounds.data ?? [], balancers.data ?? []) ?? ""}
                  className="text-sm text-brand-ink hover:text-brand-lit"
                >
                  {t("action.goTo")}
                </Link>
              </div>
            )}
          </div>
        )}
      </Part>

      <Part title={t("dns.files")}>
        <DnsPick id="dns-files" level="server" mode={draft.filesMode} onChange={(filesMode) => set({ filesMode })} />
        {draft.filesMode === "own" ? (
          <DnsServers id="dns-files-servers" mode={draft.filesMode} servers={draft.filesDns} onChange={(filesDns) => set({ filesDns })} />
        ) : draft.filesMode === "endpoint" ? (
          <Shown id="dns-files-endpoints" caption={t("dnsPick.endpoints")} value={endpoints} />
        ) : (
          <div className="hidden sm:block" />
        )}
        <DnsOutcome check={files} caption="dnsCheck.lineConfig" view="dns" />
      </Part>

      <Part title={t("dns.memory")}>
        <Count
          id="dns-cache"
          caption={t("dns.cacheSize")}
          value={draft.cacheSize}
          onChange={(v) => set({ cacheSize: v })}
        />
        <div />
        <Count id="dns-min" caption={t("dns.minTtl")} value={draft.minTtl} onChange={(v) => set({ minTtl: v })} />
        <Count id="dns-max" caption={t("dns.maxTtl")} value={draft.maxTtl} onChange={(v) => set({ maxTtl: v })} />
      </Part>

      {fault && <div className="text-sm text-alarm">{t(fault)}</div>}

      {may && (
        <div className={footer}>
          <button type="button" onClick={drop} disabled={kept === null || save.isPending} className={secondary}>
            {t("dns.cancel")}
          </button>
          <button
            type="button"
            onClick={() => void keep()}
            disabled={kept === null || unpicked || lacksServers(draft.filesMode, draft.filesDns) || save.isPending}
            className={primary}
          >
            {t("dns.save")}
          </button>
        </div>
      )}
    </>
  )
}

// The findings the choice of the files brings, told under it.
const filed = ["rule-outside", "unseen"]

// Who needs the resolver, or what stops working or misses the files with the mode picked.
function Need({
  findings,
  report,
  state,
  on,
  known,
}: {
  findings: DnsFinding[]
  report: DnsReport | undefined
  state: DnsState
  on: boolean
  known: boolean
}) {
  const t = useText()
  const language = useLanguage()
  const rules = useRules().data ?? []
  const level = levelOf(findings)

  function everyone(subject: DnsSubject): boolean {
    const rule = rules.find((one) => one.id === subject.id)

    return rule !== undefined && rule.clients.length === 0 && rule.inbounds.length === 0
  }

  if (level !== null) {
    const broken = level === "error"

    return (
      <span className={`flex items-center gap-1.5 ${broken ? "text-alarm" : "text-warn"}`}>
        <DnsMark
          findings={findings}
          view="dns"
          title={broken && !on ? t("dnsCheck.offTitle") : undefined}
          everyone={everyone}
        />
        {broken ? brokenText(t, language, findings) : missedText(t, language, findings)}
      </span>
    )
  }

  if (!on) {
    return null
  }

  if (!known || report === undefined) {
    return (
      <span className={state.needed ? "text-good" : "text-muted"}>{t(state.needed ? "dns.needed" : "dns.idle")}</span>
    )
  }

  if (report.needs.rules.length > 0 || report.needs.templates.length > 0) {
    return <span className="text-good">{t("dns.needed")}</span>
  }

  const names = [...new Set([...report.needs.picked, ...report.needs.holders].map((one) => one.name))]

  return names.length > 0 ? (
    <span className="text-good">{t("dnsCheck.neededBy", { names: fewNames(t, names) })}</span>
  ) : (
    <span className="text-muted">{t("dns.idle")}</span>
  )
}

// What stops working without the resolver: the rules, the templates and the clients counted.
function brokenText(t: Text, language: string, findings: DnsFinding[]): string {
  const rules = findings.filter((one) => one.code === "rule-off").length
  const templates = findings.filter((one) => one.code === "standins-off").length
  const clients = new Set(findings.filter((one) => one.code === "holders-off").flatMap((one) => one.clients.map((c) => c.id))).size
  const parts = [
    ...(rules > 0 ? [counted(t, language, ruleWords, rules)] : []),
    ...(templates > 0 ? [counted(t, language, templateWords, templates)] : []),
    ...(clients > 0 ? [counted(t, language, clientWords, clients)] : []),
  ]

  return t("dnsCheck.offBroken", { parts: parts.join(", ") })
}

// How many clients the resolver does not reach.
function missedText(t: Text, language: string, findings: DnsFinding[]): string {
  const clients = new Set(findings.flatMap((one) => one.clients.map((c) => c.id))).size

  return t("dnsCheck.notHanded", { clients: counted(t, language, clientsOfWords, clients) })
}
