import { useRef, useState } from "react"
import { Link, useNavigate, useSearchParams } from "react-router-dom"
import axios from "axios"
import { complaint, reason } from "@/api/auth"
import { useBalancers } from "@/api/balancers"
import { useClients } from "@/api/clients"
import { useConfigs } from "@/api/configs"
import { findingsOf, useDnsConflicts } from "@/api/dns"
import { useFailure } from "@/api/failure"
import { useMoveRule, usePlaceRule, useRules, useSwitchRule } from "@/api/rules"
import { useRoutingApply, useRoutingExport, useRoutingImport } from "@/api/exchange"
import type { ExchangeAnswer, ExchangePlan } from "@/api/exchange"
import type { Rule } from "@/api/rules"
import { scopes } from "@/api/scopes"
import { DnsMark } from "@/components/DnsMark"
import { Glyph } from "@/components/Glyph"
import { Help } from "@/components/fields"
import { Find, Knob } from "@/components/fields"
import { clientTo, configTo } from "@/components/members"
import { Move } from "@/components/Move"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { alarmBar, alarmLine, card, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Rules() {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const [params, setParams] = useSearchParams()
  const rules = useRules()
  const failed = useFailure(rules)
  const balancers = useBalancers()
  const clients = useClients().data ?? []
  const configs = useConfigs().data ?? []
  const move = useMoveRule()
  const place = usePlaceRule()
  const turn = useSwitchRule()
  const report = useDnsConflicts().data
  const may = holds(user, scopes.manageRouting)
  const find = params.get("find") ?? ""
  const all = rules.data ?? []
  const shown = all.filter((one) => matches(one, find))
  const last = all.length - 1
  const groups = new Set((balancers.data ?? []).map((one) => one.name))
  const number = new Map(all.map((one, at) => [one.id, at + 1]))

  function put(key: string, value: string) {
    const kept = new URLSearchParams(params)

    if (value === "") {
      kept.delete(key)
    } else {
      kept.set(key, value)
    }

    setParams(kept, { replace: true })
  }

  return (
    <>
    <div className={`mt-4 ${card}`}>
      {rules.isPending && failed === null && <div className="px-4 py-6 text-sm text-muted">{t("rules.loading")}</div>}
      {failed !== null && (
        <div className={all.length === 0 ? alarmLine : alarmBar}>{t(complaint(failed))}</div>
      )}
      {rules.isSuccess && all.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("rules.empty")}</div>}

      {all.length > 0 && (
        <Rows
          name="rule"
          items={shown}
          keyOf={(one) => one.id}
          drag={
            may && find === ""
              ? {
                  title: t("rules.drag"),
                  onMove: (one, to) => void place.mutateAsync({ id: one.id, to: number.get(to.id) ?? 1 }),
                }
              : undefined
          }
          tools={
            <Find value={find} onChange={(value) => put("find", value)} className="w-full wide:w-72" />
          }
          columns={[
            {
              key: "place",
              width: 40,
              caption: "#",
              sort: (one) => number.get(one.id) ?? 0,
              body: "text-faint tabular-nums",
              cell: (one) => number.get(one.id),
            },
            {
              key: "on",
              width: 60,
              caption: t("rules.on"),
              cell: (one) => (
                <Knob
                  value={one.isEnabled}
                  title={one.isEnabled ? t("rules.turnOff") : t("rules.turnOn")}
                  disabled={!may || turn.isPending}
                  onChange={(on) => void turn.mutateAsync({ id: one.id, on })}
                />
              ),
            },
            {
              key: "name",
              caption: t("rules.name"),
              sort: (one) => one.name,
              lead: true,
              body: "font-semibold text-ink",
              cell: (one) => (
                <span className="flex min-w-0 items-center gap-2">
                  {may ? (
                    <Link to={`/routing/rules/${one.id}/edit`} className="truncate hover:text-brand-ink">
                      {one.name}
                    </Link>
                  ) : (
                    <span className="truncate">{one.name}</span>
                  )}
                  <DnsMark findings={findingsOf(report, "rule", one.id)} view="rule" />
                </span>
              ),
            },
            {
              key: "action",
              width: 104,
              caption: t("rules.action"),
              sort: (one) => way(one, t),
              cell: (one) => (
                <span className="whitespace-nowrap">
                  {way(one, t)}
                  {one.action === "out" && groups.has(one.outbound) && (
                    <span className="ml-1.5 rounded bg-chip px-1.5 py-0.5 text-xs text-chip-ink">
                      {t("rules.group")}
                    </span>
                  )}
                </span>
              ),
            },
            {
              key: "targets",
              wrap: true,
              caption: t("rules.targets"),
              sort: (one) => (one.targets.length > 0 ? one.targets.join(", ") : t("rules.anything")),
              cell: (one) => <Some values={one.targets} none={t("rules.anything")} />,
            },
            {
              key: "clients",
              width: 104,
              wrap: true,
              caption: t("rules.clients"),
              sort: (one) => one.clients.join(", "),
              cell: (one) => (
                <Some values={one.clients} none={t("rules.anyone")} to={(name) => clientTo(name, clients)} />
              ),
            },
            {
              key: "inbounds",
              width: 96,
              wrap: true,
              caption: t("rules.inbounds"),
              sort: (one) => one.inbounds.join(", "),
              cell: (one) => (
                <Some values={one.inbounds} none={t("rules.anyInbound")} to={(name) => configTo(name, configs)} />
              ),
            },
            {
              key: "traffic",
              width: 96,
              caption: t("rules.traffic"),
              sort: (one) => traffic(one, t),
              cell: (one) => traffic(one, t),
            },
            {
              key: "ranges",
              width: 120,
              caption: t("rules.ranges"),
              sort: (one) => `${one.state.ranges} / ${one.state.names}`,
              cell: (one) => `${one.state.ranges} / ${one.state.names}`,
            },
            {
              key: "state",
              width: 128,
              caption: t("rules.state"),
              sort: (one) => ranked(one),
              cell: (one) => <State rule={one} t={t} />,
            },
            {
              key: "actions",
              width: 128,
              caption: t("rules.actions"),
              tail: true,
              cell: (one) =>
                may && (
                  <div className="flex items-center justify-end gap-1">
                    <Move
                      first={(number.get(one.id) ?? 1) === 1}
                      last={(number.get(one.id) ?? 1) - 1 === last}
                      upTitle={t("rules.up")}
                      downTitle={t("rules.down")}
                      onMove={(up) => void move.mutateAsync({ id: one.id, up })}
                    />
                    <RowActions
                      title={t("rules.actions")}
                      actions={[
                        { label: t("action.settings"), onPick: () => navigate(`/routing/rules/${one.id}/edit`) },
                      ]}
                    />
                  </div>
                ),
            },
          ]}
        />
      )}
    </div>
    <Exchange may={may} />
    </>
  )
}

function Exchange({ may }: { may: boolean }) {
  const t = useText()
  const take = useRoutingExport()
  const read = useRoutingImport()
  const put = useRoutingApply()
  const picker = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [plan, setPlan] = useState<ExchangePlan | null>(null)
  const failed = take.error ?? read.error ?? put.error

  function go(picked: File) {
    setFile(picked)
    setPlan(null)
    put.reset()
    take.reset()
    void read
      .mutateAsync({ file: picked })
      .then(setPlan)
      .catch(() => undefined)
  }

  function reset() {
    setFile(null)
    setPlan(null)
    read.reset()
    put.reset()
  }

  return (
    <div className={`mt-4 flex flex-col gap-3 p-4 ${card}`}>
      <div className="flex items-center gap-2 text-sm font-medium text-ink">
        <Glyph name="fork" />
        {t("exchange.title")}
        <Help text={t("exchange.about")} />
      </div>

      {put.isSuccess ? (
        <>
          <div className="text-sm font-semibold text-ink">{t("exchange.done")}</div>
          <Answer answer={put.data} t={t} />
          <div className="flex flex-wrap gap-2">
            <button type="button" className={secondary} onClick={reset}>
              {t("exchange.close")}
            </button>
          </div>
        </>
      ) : plan !== null && file !== null ? (
        <>
          <div className="text-sm font-semibold text-ink">{t("exchange.plan")}</div>
          <div className="flex flex-wrap items-baseline gap-x-3 text-sm">
            <span className="truncate text-ink" title={file.name}>
              {file.name}
            </span>
            <span className="text-muted">
              {t("exchange.file", {
                rules: plan.doc.rules.length,
                presets: plan.doc.presets.length,
                sources: plan.doc.sources.length,
              })}
            </span>
          </div>
          <Plan plan={plan} t={t} />
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              className={primary}
              disabled={put.isPending}
              onClick={() => void put.mutateAsync({ file }).catch(() => undefined)}
            >
              {put.isPending ? t("exchange.busy") : t("exchange.apply")}
            </button>
            <button type="button" className={secondary} disabled={put.isPending} onClick={reset}>
              {t("exchange.cancel")}
            </button>
          </div>
        </>
      ) : (
        <div className="flex flex-wrap gap-2">
          <button type="button" className={primary} disabled={take.isPending} onClick={() => take.mutate()}>
            {take.isPending ? t("exchange.busy") : t("exchange.export")}
          </button>
          {may && (
            <button type="button" className={secondary} disabled={read.isPending} onClick={() => picker.current?.click()}>
              {t("exchange.pick")}
            </button>
          )}
        </div>
      )}

      <input
        ref={picker}
        type="file"
        accept=".json,application/json"
        className="hidden"
        onChange={(event) => {
          const picked = event.target.files?.[0]
          event.target.value = ""
          if (picked !== undefined) {
            go(picked)
          }
        }}
      />

      {failed !== null && (
        <div className="text-sm text-alarm">
          {t(complaint(failed))}
          {said(failed).length > 0 && <div className="mt-0.5 text-xs text-muted">{said(failed)}</div>}
        </div>
      )}
    </div>
  )
}

function Plan({ plan, t }: { plan: ExchangePlan; t: Text }) {
  const added = [
    ...plan.newRules.map((name) => t("exchange.itemRule", { name })),
    ...plan.newPresets.map((name) => t("exchange.itemPreset", { name })),
    ...plan.newSources.map((name) => t("exchange.itemSource", { name })),
  ]
  const replaced = [
    ...plan.replacedRules.map((name) => t("exchange.itemRule", { name })),
    ...plan.replacedPresets.map((name) => t("exchange.itemPreset", { name })),
    ...plan.replacedSources.map((name) => t("exchange.itemSource", { name })),
  ]

  return (
    <div className="grid gap-4 wide:grid-cols-2">
      <div className="flex flex-col gap-1.5 text-sm">
        <div className="font-semibold text-ink">{t("exchange.added")}</div>
        {added.length === 0 ? (
          <div className="text-muted">{t("exchange.none")}</div>
        ) : (
          added.map((one, at) => (
            <div key={at}>{one}</div>
          ))
        )}
        <div className="mt-2 font-semibold text-ink">{t("exchange.replaced")}</div>
        {replaced.length === 0 ? (
          <div className="text-muted">{t("exchange.none")}</div>
        ) : (
          replaced.map((one, at) => (
            <div key={at}>{one}</div>
          ))
        )}
      </div>
      <div className="flex flex-col gap-1.5 text-sm">
        <div className="font-semibold text-ink">{t("exchange.rest")}</div>
        <div className="text-muted">{t("exchange.restNote")}</div>
        <div className="mt-2 font-semibold text-ink">{t("exchange.basic")}</div>
        <div className="text-muted">{plan.replacesBasic ? t("exchange.basicOver") : t("exchange.basicNone")}</div>
        {plan.detached.length > 0 && (
          <div className="mt-2 rounded-lg bg-warn-soft px-3 py-2 text-[13px] text-warn">
            <div className="font-semibold">{t("exchange.detached")}</div>
            {plan.detached.map((one) => (
              <div key={one}>{one}</div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

function Answer({ answer, t }: { answer: ExchangeAnswer; t: Text }) {
  return (
    <div className="flex flex-col gap-2 text-sm">
      <AnswerRow head={t("exchange.took")} lines={answer.added} />
      <AnswerRow head={t("exchange.swapped")} lines={answer.replaced} />
      {answer.basicReplaced && <div className="text-muted">{t("exchange.basicOver")}</div>}
      <AnswerRow head={t("exchange.refused")} lines={answer.refused} alarm />
      <AnswerRow head={t("exchange.still")} lines={answer.detached} />
    </div>
  )
}

function AnswerRow({ head, lines, alarm }: { head: string; lines: string[]; alarm?: boolean }) {
  if (lines.length === 0) {
    return null
  }

  return (
    <div className="flex flex-col gap-0.5">
      <div className={alarm ? "font-semibold text-alarm" : "font-semibold text-ink"}>{head}</div>
      {lines.map((one) => (
        <div key={one} className="text-body">
          {one}
        </div>
      ))}
    </div>
  )
}

function said(error: unknown): string {
  if (!axios.isAxiosError(error)) {
    return ""
  }

  return (error.response?.data as { message?: string } | undefined)?.message ?? ""
}

function Some({
  values,
  none,
  to,
}: {
  values: string[]
  none: string
  to?: (value: string) => string | undefined
}) {
  if (values.length === 0) {
    return <span className="text-muted">{none}</span>
  }

  const link = to?.(values[0])

  return (
    <span className="flex min-w-0 items-center gap-1.5" title={values.join("\n")}>
      {link === undefined ? (
        <span className="truncate">{values[0]}</span>
      ) : (
        <Link to={link} className="truncate text-brand-ink hover:text-brand-lit">
          {values[0]}
        </Link>
      )}
      {values.length > 1 && (
        <span className="shrink-0 rounded bg-chip px-1.5 py-0.5 text-xs text-chip-ink">+{values.length - 1}</span>
      )}
    </span>
  )
}

function way(rule: Rule, t: Text): string {
  if (rule.action === "block") {
    return t("rules.actionBlock")
  }

  return rule.action === "direct" ? t("rules.actionDirect") : rule.outbound
}

function ranked(rule: Rule): number {
  if (rule.state.fault.length > 0) {
    return 1
  }

  return rule.state.isLive ? 0 : 2
}

function State({ rule, t }: { rule: Rule; t: Text }) {
  if (rule.state.isHeld) {
    return (
      <span className="text-warn" title={rule.state.message}>
        {t("rules.holding")}
      </span>
    )
  }

  if (rule.state.fault.length > 0) {
    return (
      <span className="text-alarm" title={rule.state.message}>
        {t(reason(rule.state.fault))}
      </span>
    )
  }

  if (!rule.state.isLive) {
    return <span className="text-muted">{t("rules.off")}</span>
  }

  return <span className="text-good">{t("rules.live")}</span>
}

function traffic(rule: Rule, t: Text): string {
  const protocol = rule.protocol === "any" ? t("rules.protocolAny") : rule.protocol.toUpperCase()
  const to = rule.ports.length > 0 ? `${protocol} ${rule.ports.join(", ")}` : protocol

  return rule.sourcePorts.length > 0 ? `${to} ${t("rules.fromPorts", { ports: rule.sourcePorts.join(", ") })}` : to
}

function matches(one: Rule, find: string): boolean {
  const query = find.trim().toLowerCase()

  return (
    query === "" ||
    one.name.toLowerCase().includes(query) ||
    one.outbound.toLowerCase().includes(query) ||
    one.targets.some((target) => target.toLowerCase().includes(query)) ||
    one.clients.some((name) => name.toLowerCase().includes(query)) ||
    one.inbounds.some((name) => name.toLowerCase().includes(query))
  )
}
