import { useState } from "react"
import { draftOf, useResolver, useSaveResolver } from "@/api/dns"
import type { DnsDraft, DnsState, Resolver as ResolverSettings } from "@/api/dns"
import { complaint } from "@/api/auth"
import { scopes } from "@/api/scopes"
import { Count, Flag, Line, Section } from "@/components/fields"
import { card, primary, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { dnsDrafted, same } from "@/store/draftSlice"
import { useAppDispatch, useAppSelector } from "@/store/hooks"

export function Dns() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const resolver = useResolver()
  const may = holds(user, scopes.manageRouting)

  return (
    <div>
      <h1 className="text-xl font-semibold">{t("nav.dns")}</h1>

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
    <div className={`mt-4 px-4 py-3 ${card}`}>
      <div className="flex flex-wrap items-baseline gap-x-6 gap-y-2">
        <span className={state.isRunning ? "text-sm font-medium text-brand-ink" : "text-sm font-medium text-muted"}>
          {t(state.isRunning ? "dns.running" : "dns.stopped")}
        </span>
        {state.listening.length > 0 && <Tally caption={t("dns.listening")} value={state.listening.join(", ")} />}
        <Tally caption={t("dns.questions")} value={state.questions.toLocaleString(language)} />
        <Tally caption={t("dns.fromMemory")} value={state.cached.toLocaleString(language)} />
        <Tally caption={t("dns.unanswered")} value={state.failed.toLocaleString(language)} />
        <Tally caption={t("dns.added")} value={state.added.toLocaleString(language)} />
        <Tally caption={t("dns.waiting")} value={state.waiting.toLocaleString(language)} />
        <Tally caption={t("dns.names")} value={state.names.toLocaleString(language)} />
      </div>

      {state.fault.length > 0 && <div className="mt-2 text-sm text-alarm">{state.fault}</div>}
    </div>
  )
}

function Editor({ settings, may }: { settings: ResolverSettings; may: boolean }) {
  const t = useText()
  const dispatch = useAppDispatch()
  const kept = useAppSelector((s) => s.drafts.dns)
  const [fault, setFault] = useState<TextKey | null>(null)
  const save = useSaveResolver()
  const saved = draftOf(settings)
  const draft = kept ?? saved

  function set(part: Partial<DnsDraft>) {
    const next = { ...draft, ...part }
    dispatch(dnsDrafted(same(next, saved) ? null : next))
  }

  async function keep() {
    setFault(null)
    try {
      await save.mutateAsync(draft)
      dispatch(dnsDrafted(null))
    } catch (error) {
      setFault(complaint(error))
    }
  }

  function drop() {
    setFault(null)
    dispatch(dnsDrafted(null))
  }

  return (
    <div className={`mt-4 flex flex-col gap-4 px-4 py-4 ${card}`}>
      <Section title={t("dns.serving")}>
        <div className="col-span-2">
          <Flag id="dns-on" caption={t("dns.enabled")} value={draft.isEnabled} onChange={(v) => set({ isEnabled: v })} />
        </div>
        <Count id="dns-port" caption={t("dns.port")} value={draft.port} onChange={(v) => set({ port: v })} />
        <Count
          id="dns-life"
          caption={t("dns.nameMinutes")}
          value={draft.nameMinutes}
          onChange={(v) => set({ nameMinutes: v })}
        />
        <Line
          id="dns-listen"
          caption={t("dns.listen")}
          value={draft.listen.join(", ")}
          onChange={(v) => set({ listen: parts(v) })}
          wide
        />
        <Line
          id="dns-upstreams"
          caption={t("dns.upstreams")}
          value={draft.upstreams.join(", ")}
          onChange={(v) => set({ upstreams: parts(v) })}
          wide
        />
      </Section>

      <Section title={t("dns.memory")}>
        <Count id="dns-cache" caption={t("dns.cacheSize")} value={draft.cacheSize} onChange={(v) => set({ cacheSize: v })} />
        <div />
        <Count id="dns-min" caption={t("dns.minTtl")} value={draft.minTtl} onChange={(v) => set({ minTtl: v })} />
        <Count id="dns-max" caption={t("dns.maxTtl")} value={draft.maxTtl} onChange={(v) => set({ maxTtl: v })} />
      </Section>

      <Section title={t("dns.clients")}>
        <div className="col-span-2 flex flex-col gap-2">
          <Flag
            id="dns-intercept"
            caption={t("dns.intercept")}
            value={draft.intercept}
            onChange={(v) => set({ intercept: v })}
          />
          <Flag id="dns-dot" caption={t("dns.blockDot")} value={draft.blockDot} onChange={(v) => set({ blockDot: v })} />
          <Flag id="dns-doh" caption={t("dns.blockDoh")} value={draft.blockDoh} onChange={(v) => set({ blockDoh: v })} />
        </div>
      </Section>

      {fault && <div className="text-sm text-alarm">{t(fault)}</div>}

      {may && (
        <div className="flex justify-end gap-2 border-t border-line pt-3">
          <button type="button" onClick={drop} disabled={kept === null || save.isPending} className={secondary}>
            {t("dns.cancel")}
          </button>
          <button
            type="button"
            onClick={() => void keep()}
            disabled={kept === null || save.isPending}
            className={primary}
          >
            {t("dns.save")}
          </button>
        </div>
      )}
    </div>
  )
}

function Tally({ caption, value }: { caption: string; value: string }) {
  return (
    <div className="text-sm">
      <span className="text-muted">{caption}</span> <span className="font-medium">{value}</span>
    </div>
  )
}

function parts(text: string): string[] {
  return text
    .split(/[,\s]+/)
    .map((one) => one.trim())
    .filter((one) => one.length > 0)
}
