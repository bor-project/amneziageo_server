import { useState } from "react"
import { complaint } from "@/api/auth"
import { draftOf, useResolver, useSaveResolver } from "@/api/dns"
import type { DnsDraft, DnsState, Resolver as ResolverSettings } from "@/api/dns"
import { scopes } from "@/api/scopes"
import { Box, Count, Flag, Line, Part } from "@/components/fields"
import { card, primary, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { dnsDrafted, same } from "@/store/draftSlice"
import { useAppDispatch, useAppSelector } from "@/store/hooks"

export function Dns() {
  const user = useAppSelector((s) => s.auth.user)
  const resolver = useResolver()
  const may = holds(user, scopes.manageRouting)

  return (
    <div className="mt-4 flex flex-col gap-4">
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
    <>
      <Part title={t("dns.serving")}>
        <div className="sm:col-span-2">
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

      <Part title={t("dns.clients")}>
        <div className="flex flex-col gap-2 sm:col-span-2">
          <Flag
            id="dns-intercept"
            caption={t("dns.intercept")}
            value={draft.intercept}
            onChange={(v) => set({ intercept: v })}
          />
          <Flag id="dns-dot" caption={t("dns.blockDot")} value={draft.blockDot} onChange={(v) => set({ blockDot: v })} />
          <Flag id="dns-doh" caption={t("dns.blockDoh")} value={draft.blockDoh} onChange={(v) => set({ blockDoh: v })} />
        </div>
      </Part>

      {fault && <div className="text-sm text-alarm">{t(fault)}</div>}

      {may && (
        <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
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
    </>
  )
}

function parts(text: string): string[] {
  return text
    .split(/[,\s]+/)
    .map((one) => one.trim())
    .filter((one) => one.length > 0)
}
