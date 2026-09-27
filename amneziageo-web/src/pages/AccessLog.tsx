import { useState } from "react"
import type { KeyboardEvent, ReactNode } from "react"
import { useAccess, useAccessRecords, useAccessSummary, useClearAccess, useSaveAccess } from "@/api/access"
import type { AccessCount, AccessFilter, AccessGroup, AccessRecord, AccessSummary, Grouping, Outcome, Verdict } from "@/api/access"
import { complaint } from "@/api/auth"
import { useClients } from "@/api/clients"
import { useOutbounds } from "@/api/outbounds"
import { Knob } from "@/components/fields"
import { Rows } from "@/components/Rows"
import type { Column } from "@/components/Rows"
import { card, fieldBox, label, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"

type Period = "hour" | "hours6" | "day" | "week" | "range"

type Shown = "summary" | "records"

type Stop = "block" | "held" | "guard"

type Words = Record<Intl.LDMLPluralRule, TextKey>

interface View {
  period: Period
  since: string
  until: string
  client: string
  how: string
  outcome: string
  search: string
}

const hours: Record<Exclude<Period, "range">, number> = { hour: 1, hours6: 6, day: 24, week: 168 }

const periods: Period[] = ["hour", "hours6", "day", "week", "range"]

const failures: Outcome[] = ["silent", "reset", "empty", "unreachable"]

const stops: Stop[] = ["block", "held", "guard"]

const groupings: Grouping[] = ["domain", "name", "rule", "way", "client"]

const start: View = { period: "hour", since: "", until: "", client: "", how: "", outcome: "", search: "" }

const small = "h-8 rounded-md border border-line-input bg-input px-2 text-xs text-ink outline-none focus:border-brand"

const dayWords: Words = {
  zero: "access.dayMany",
  one: "access.dayOne",
  two: "access.dayFew",
  few: "access.dayFew",
  many: "access.dayMany",
  other: "access.dayOther",
}

const recordWords: Words = {
  zero: "access.recordMany",
  one: "access.recordOne",
  two: "access.recordFew",
  few: "access.recordFew",
  many: "access.recordMany",
  other: "access.recordOther",
}

const outcomeWords: Record<Outcome, TextKey> = {
  ok: "access.ok",
  empty: "access.empty",
  reset: "access.reset",
  silent: "access.silent",
  unreachable: "access.unreachable",
}

const keyWords: Record<Grouping, TextKey> = {
  domain: "access.domain",
  name: "access.name",
  rule: "access.rule",
  way: "access.way",
  client: "access.client",
}

const byWords: Record<Grouping, TextKey> = {
  domain: "access.byDomain",
  name: "access.byName",
  rule: "access.byRule",
  way: "access.byWay",
  client: "access.byClient",
}

export function AccessHead({ lead }: { lead: ReactNode }) {
  const t = useText()
  const language = useLanguage()
  const state = useAccess()
  const save = useSaveAccess()
  const clear = useClearAccess()
  const [days, setDays] = useState<string | null>(null)
  const [asking, setAsking] = useState(false)
  const [fault, setFault] = useState<TextKey | null>(null)
  const held = state.data

  async function change(part: { isEnabled?: boolean; days?: number }) {
    setFault(null)
    try {
      await save.mutateAsync(part)
    } catch (error) {
      setFault(complaint(error))
    }
  }

  function keepDays() {
    if (days === null || held === undefined) {
      return
    }

    setDays(null)
    if (days.trim() !== "" && Number(days) !== held.days) {
      void change({ days: Number(days) })
    }
  }

  async function wipe() {
    setAsking(false)
    setFault(null)
    try {
      await clear.mutateAsync()
    } catch (error) {
      setFault(complaint(error))
    }
  }

  return (
    <div className={`flex flex-col gap-2.5 px-4 py-3 ${card}`}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        {lead}
        {held && (
          <div className="flex items-center gap-2.5">
            <span className="text-sm text-ink">{t("access.record")}</span>
            <Knob
              value={held.isEnabled}
              title={t("access.record")}
              disabled={save.isPending}
              onChange={(value) => void change({ isEnabled: value })}
            />
          </div>
        )}
      </div>

      {held && (
        <div className="flex flex-wrap items-center gap-x-1.5 gap-y-1 text-xs text-muted">
          <label htmlFor="access-days">{t("access.keep")}</label>
          <input
            id="access-days"
            type="number"
            min={1}
            max={90}
            value={days ?? String(held.days)}
            onChange={(e) => setDays(e.target.value)}
            onBlur={keepDays}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                keepDays()
              }
            }}
            className={`w-14 ${small}`}
          />
          <span>{t(plural(language, dayWords, Number(days ?? held.days)))}</span>
          <span aria-hidden>·</span>
          <span>
            {t(plural(language, recordWords, held.records), { count: held.records.toLocaleString(language) })}
            {held.oldest !== null && ` ${t("access.since", { time: day(held.oldest, language) })}`}
          </span>
          {held.lost > 0 && (
            <>
              <span aria-hidden>·</span>
              <span>{t("access.lost", { count: held.lost.toLocaleString(language) })}</span>
            </>
          )}
          <span aria-hidden>·</span>
          {asking ? (
            <span className="flex items-center gap-2">
              <span className="text-alarm">{t("access.clearAsk")}</span>
              <button type="button" onClick={() => void wipe()} className="text-alarm underline hover:text-alarm-lit">
                {t("action.yes")}
              </button>
              <button type="button" onClick={() => setAsking(false)} className="underline hover:text-ink">
                {t("action.no")}
              </button>
            </span>
          ) : (
            <button
              type="button"
              onClick={() => setAsking(true)}
              disabled={held.records === 0 || clear.isPending}
              className="underline hover:text-ink disabled:no-underline disabled:opacity-50"
            >
              {t("access.clear")}
            </button>
          )}
        </div>
      )}

      {held && held.isEnabled && held.fault.length > 0 && <div className="text-sm text-alarm">{held.fault}</div>}
      {fault && <div className="text-sm text-alarm">{t(fault)}</div>}
      {state.error && <div className="text-sm text-alarm">{t(complaint(state.error))}</div>}
    </div>
  )
}

export function AccessLog() {
  const t = useText()
  const language = useLanguage()
  const state = useAccess()
  const clients = useClients()
  const outbounds = useOutbounds()
  const [draft, setDraft] = useState<View>(start)
  const [applied, setApplied] = useState(() => ({ view: start, at: Date.now() }))
  const [shown, setShown] = useState<Shown>("summary")
  const [by, setBy] = useState<Grouping>("domain")
  const filter = filterOf(applied.view, applied.at)
  const summary = useAccessSummary(filter, by)
  const records = useAccessRecords(filter, shown === "records")
  const collator = new Intl.Collator(language, { numeric: true, sensitivity: "base" })
  const names = (clients.data ?? []).map((one) => one.name).sort(collator.compare)
  const channels = (outbounds.data ?? []).map((one) => one.name)
  const found = summary.data

  function apply(next: View) {
    setDraft(next)
    setApplied({ view: next, at: Date.now() })
  }

  function settle() {
    if (JSON.stringify(draft) !== JSON.stringify(applied.view)) {
      apply(draft)
    }
  }

  function enter(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter") {
      apply(draft)
    }
  }

  function refresh() {
    apply(draft)
    void state.refetch()
  }

  function period(value: Period) {
    const now = new Date()
    const opening = value === "range" && draft.since === ""

    apply({
      ...draft,
      period: value,
      since: opening ? local(new Date(now.getTime() - 3600000)) : draft.since,
      until: opening ? local(now) : draft.until,
    })
  }

  return (
    <>
      <div className={`flex flex-col gap-3.5 p-4 ${card}`}>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 wide:grid-cols-[10rem_11rem_11rem_10rem_minmax(0,1fr)_auto] wide:items-end">
          <Field id="access-period" caption={t("access.period")}>
            <select
              id="access-period"
              value={draft.period}
              onChange={(e) => period(e.target.value as Period)}
              className={`w-full ${fieldBox}`}
            >
              {periods.map((one) => (
                <option key={one} value={one}>
                  {t(`access.${one}`)}
                </option>
              ))}
            </select>
          </Field>

          <Field id="access-client" caption={t("access.client")}>
            <select
              id="access-client"
              value={draft.client}
              onChange={(e) => apply({ ...draft, client: e.target.value })}
              className={`w-full ${fieldBox}`}
            >
              <option value="">{t("access.all")}</option>
              {names.map((name) => (
                <option key={name} value={name}>
                  {name}
                </option>
              ))}
            </select>
          </Field>

          <Field id="access-how" caption={t("access.how")}>
            <select
              id="access-how"
              value={draft.how}
              onChange={(e) => apply({ ...draft, how: e.target.value })}
              className={`w-full ${fieldBox}`}
            >
              <option value="">{t("access.all")}</option>
              <option value="local">{t("access.local")}</option>
              <option value="relay">{t("access.relay")}</option>
              {channels.map((name) => (
                <option key={name} value={`way:${name}`}>
                  {t("access.through", { name })}
                </option>
              ))}
              {stops.map((one) => (
                <option key={one} value={one}>
                  {t(`access.${one}`)}
                </option>
              ))}
            </select>
          </Field>

          <Field id="access-outcome" caption={t("access.outcome")}>
            <select
              id="access-outcome"
              value={draft.outcome}
              onChange={(e) => apply({ ...draft, outcome: e.target.value })}
              className={`w-full ${fieldBox}`}
            >
              <option value="">{t("access.all")}</option>
              <option value="ok">{t("access.ok")}</option>
              <option value="failed">{t("access.failed")}</option>
              {failures.map((one) => (
                <option key={one} value={one}>
                  {t(outcomeWords[one])}
                </option>
              ))}
            </select>
          </Field>

          <Field id="access-search" caption={t("access.search")}>
            <input
              id="access-search"
              value={draft.search}
              onChange={(e) => setDraft({ ...draft, search: e.target.value })}
              onKeyDown={enter}
              onBlur={settle}
              className={`w-full ${fieldBox}`}
            />
          </Field>

          <button type="button" onClick={refresh} className={secondary}>
            {t("diagnostics.refresh")}
          </button>
        </div>

        {draft.period === "range" && (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 wide:grid-cols-[14rem_14rem]">
            <Field id="access-from" caption={t("access.from")}>
              <input
                id="access-from"
                type="datetime-local"
                value={draft.since}
                onChange={(e) => setDraft({ ...draft, since: e.target.value })}
                onKeyDown={enter}
                onBlur={settle}
                className={`w-full ${fieldBox}`}
              />
            </Field>
            <Field id="access-to" caption={t("access.to")}>
              <input
                id="access-to"
                type="datetime-local"
                value={draft.until}
                onChange={(e) => setDraft({ ...draft, until: e.target.value })}
                onKeyDown={enter}
                onBlur={settle}
                className={`w-full ${fieldBox}`}
              />
            </Field>
          </div>
        )}

        {found && <Tiles found={found} />}

        {found && found.ways.length > 0 && (
          <div className="flex flex-wrap items-center gap-1.5 text-xs text-muted">
            <span className="mr-0.5">{t("access.channels")}</span>
            {found.ways.map((one) => (
              <button
                key={one.key}
                type="button"
                onClick={() => apply({ ...draft, how: `way:${one.key}` })}
                className="rounded-md bg-chip px-2 py-0.5 text-chip-ink hover:bg-picked"
              >
                {one.key} · {one.count.toLocaleString(language)}
              </button>
            ))}
          </div>
        )}

        {found && found.outcomes.some((one) => one.key !== "ok") && (
          <div className="flex flex-wrap items-center gap-1.5 text-xs text-muted">
            <span className="mr-0.5">{t("access.failures")}</span>
            {found.outcomes
              .filter((one) => one.key !== "ok")
              .map((one) => (
                <button
                  key={one.key}
                  type="button"
                  onClick={() => apply({ ...draft, outcome: one.key })}
                  className="rounded-md bg-alarm-soft px-2 py-0.5 text-alarm hover:bg-active"
                >
                  {t(outcomeWords[one.key as Outcome] ?? "access.failed")} · {one.count.toLocaleString(language)}
                </button>
              ))}
          </div>
        )}

        {summary.error && <div className="text-sm text-alarm">{t(complaint(summary.error))}</div>}
      </div>

      <div className={card}>
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-4 py-3">
          <Toggle
            value={shown}
            options={[
              ["summary", t("access.summary")],
              ["records", t("access.records")],
            ]}
            onChange={setShown}
          />
          {shown === "summary" && (
            <div className="flex items-center gap-2 text-xs text-muted">
              <label htmlFor="access-by">{t("access.group")}</label>
              <select
                id="access-by"
                value={by}
                onChange={(e) => setBy(e.target.value as Grouping)}
                className={small}
              >
                {groupings.map((one) => (
                  <option key={one} value={one}>
                    {t(byWords[one])}
                  </option>
                ))}
              </select>
            </div>
          )}
        </div>

        {shown === "summary" ? (
          <Summary found={found} by={by} />
        ) : (
          <Records
            items={records.data?.pages.flat()}
            more={records.hasNextPage}
            loading={records.isFetchingNextPage}
            onMore={() => void records.fetchNextPage()}
          />
        )}

        {shown === "records" && records.error && (
          <div className="px-4 py-3 text-sm text-alarm">{t(complaint(records.error))}</div>
        )}
      </div>
    </>
  )
}

function Field({ id, caption, children }: { id: string; caption: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <label className={label} htmlFor={id}>
        {caption}
      </label>
      <div className="mt-1">{children}</div>
    </div>
  )
}

function Tiles({ found }: { found: AccessSummary }) {
  const t = useText()
  const ok = counted(found.outcomes, "ok")
  const failed = failures.reduce((sum, one) => sum + counted(found.outcomes, one), 0)
  const stopped = stops.reduce((sum, one) => sum + counted(found.verdicts, one), 0)

  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
      <Tile caption={t("access.count")} value={found.total} />
      <Tile caption={t("access.localCaption")} value={counted(found.paths, "local")} whole={found.total} />
      <Tile caption={t("access.relayCaption")} value={counted(found.paths, "relay")} whole={found.total} />
      <Tile caption={t("access.stoppedCaption")} value={stopped} whole={found.total} />
      <Tile caption={t("access.okCaption")} value={ok} whole={ok + failed} tone="text-good" />
      <Tile caption={t("access.failedCaption")} value={failed} whole={ok + failed} tone="text-alarm" />
    </div>
  )
}

function Tile({
  caption,
  value,
  whole,
  tone = "text-ink",
}: {
  caption: string
  value: number
  whole?: number
  tone?: string
}) {
  const language = useLanguage()

  return (
    <div className="rounded-lg bg-hover px-3 py-2">
      <div className="text-xs text-muted">{caption}</div>
      <div className={`text-lg font-medium ${value > 0 ? tone : "text-ink"}`}>
        {value.toLocaleString(language)}
        {whole !== undefined && whole > 0 && (
          <span className="ml-1.5 text-xs font-normal text-muted">{Math.round((value / whole) * 100)}%</span>
        )}
      </div>
    </div>
  )
}

function Toggle<T extends string>({
  value,
  options,
  onChange,
}: {
  value: T
  options: [T, string][]
  onChange: (value: T) => void
}) {
  return (
    <div className="flex gap-0.5 rounded-lg border border-line-input bg-input p-0.5">
      {options.map(([key, caption]) => (
        <button
          key={key}
          type="button"
          aria-pressed={value === key}
          onClick={() => onChange(key)}
          className={`rounded-md px-3 py-1.25 text-[13px] ${value === key ? "bg-chip font-medium text-chip-ink" : "text-muted hover:text-ink"}`}
        >
          {caption}
        </button>
      ))}
    </div>
  )
}

function Summary({ found, by }: { found: AccessSummary | undefined; by: Grouping }) {
  const t = useText()
  const language = useLanguage()

  if (found === undefined) {
    return null
  }

  if (found.groups.length === 0) {
    return <div className="px-4 py-6 text-sm text-muted">{t("diagnostics.empty")}</div>
  }

  const columns: (Column<AccessGroup> | false)[] = [
    {
      key: "key",
      caption: t(keyWords[by]),
      lead: true,
      sort: (group) => group.key,
      cell: (group) =>
        group.key.length === 0 ? (
          <span className="text-muted">{t("access.noRule")}</span>
        ) : (
          <span className={address(group.key) ? "text-muted" : "text-ink"}>{group.key}</span>
        ),
    },
    {
      key: "how",
      caption: t("access.how"),
      width: 184,
      sort: (group) => look(t, group.verdict, group.way)[0],
      cell: (group) => <How verdict={group.verdict} way={group.way} via={group.via} path="" />,
    },
    by !== "rule" && {
      key: "rule",
      caption: t("access.rule"),
      width: 160,
      sort: (group) => group.ruleName,
      cell: (group) => <Rule name={group.ruleName} />,
    },
    by !== "client" && {
      key: "clients",
      caption: t("access.clients"),
      width: 96,
      sort: (group) => group.clients,
      cell: (group) => group.clients.toLocaleString(language),
    },
    {
      key: "count",
      caption: t("access.count"),
      width: 120,
      sort: (group) => group.count,
      cell: (group) => group.count.toLocaleString(language),
    },
    {
      key: "ok",
      caption: t("access.okCaption"),
      width: 104,
      sort: (group) => group.ok,
      cell: (group) => <Share value={group.ok} tone="text-good" />,
    },
    {
      key: "failed",
      caption: t("access.failedCaption"),
      width: 96,
      sort: (group) => group.failed,
      cell: (group) => <Share value={group.failed} tone="text-alarm" />,
    },
    {
      key: "last",
      caption: t("access.last"),
      width: 168,
      sort: (group) => Date.parse(group.last),
      cell: (group) => moment(group.last, language),
    },
  ]

  return (
    <>
      <Rows
        name="group"
        items={found.groups}
        columns={columns.filter((column): column is Column<AccessGroup> => column !== false)}
        keyOf={(group) => `${group.key}|${group.verdict}|${group.way}|${group.via}|${group.ruleName}`}
      />
      {found.isCut && (
        <div className="border-t border-line-soft px-4 py-2.5 text-xs text-muted">
          {t("access.cut", { count: found.groups.length.toLocaleString(language) })}
        </div>
      )}
    </>
  )
}

function Records({
  items,
  more,
  loading,
  onMore,
}: {
  items: AccessRecord[] | undefined
  more: boolean
  loading: boolean
  onMore: () => void
}) {
  const t = useText()
  const language = useLanguage()

  if (items === undefined) {
    return null
  }

  if (items.length === 0) {
    return <div className="px-4 py-6 text-sm text-muted">{t("diagnostics.empty")}</div>
  }

  const columns: Column<AccessRecord>[] = [
    {
      key: "at",
      caption: t("access.time"),
      width: 152,
      sort: (record) => Date.parse(record.at),
      cell: (record) => moment(record.at, language),
    },
    {
      key: "client",
      caption: t("access.client"),
      width: 152,
      sort: (record) => (record.client.length > 0 ? record.client : record.source),
      cell: (record) =>
        record.client.length > 0 ? record.client : <span className="text-muted">{record.source}</span>,
    },
    {
      key: "target",
      caption: t("access.target"),
      lead: true,
      sort: (record) => (record.name.length > 0 ? record.name : record.target),
      cell: (record) => <Target record={record} />,
    },
    {
      key: "rule",
      caption: t("access.rule"),
      width: 152,
      sort: (record) => record.ruleName,
      cell: (record) => <Rule name={record.ruleName} />,
    },
    {
      key: "how",
      caption: t("access.how"),
      width: 200,
      sort: (record) => look(t, record.verdict, record.way)[0],
      cell: (record) => <How verdict={record.verdict} way={record.way} via={record.via} path={record.path} />,
    },
    {
      key: "outcome",
      caption: t("access.outcome"),
      width: 120,
      sort: (record) => record.outcome,
      cell: (record) => (record.outcome === "" ? null : <Result outcome={record.outcome} />),
    },
  ]

  return (
    <>
      <Rows name="record" items={items} columns={columns} keyOf={(record) => record.id} />
      {more && (
        <div className="flex justify-center border-t border-line-soft px-4 py-3">
          <button type="button" onClick={onMore} disabled={loading} className={secondary}>
            {t("access.more")}
          </button>
        </div>
      )}
    </>
  )
}

function Target({ record }: { record: AccessRecord }) {
  const place = spot(record)
  const kind = protocolName(record.protocol)

  if (record.name.length > 0) {
    return (
      <>
        <span className="text-ink">{record.name}</span>{" "}
        <span className="text-muted">
          {place} {kind}
        </span>
      </>
    )
  }

  return (
    <>
      <span className="text-ink">{place}</span> <span className="text-muted">{kind}</span>
    </>
  )
}

function Rule({ name }: { name: string }) {
  const t = useText()

  return name.length > 0 ? <>{name}</> : <span className="text-muted">{t("access.noRule")}</span>
}

function How({ verdict, way, via, path }: { verdict: Verdict; way: string; via: string; path: string }) {
  const t = useText()
  const [text, tone] = look(t, verdict, way)
  const extra = aside(t, verdict, way, path)

  return (
    <span className="inline-flex max-w-full items-center gap-1.5" title={via.length > 0 ? `${via} → ${way}` : undefined}>
      <span className={`truncate rounded-md px-2 py-0.5 text-xs ${tone}`}>{text}</span>
      {extra.length > 0 && <span className="truncate text-xs text-muted">{extra}</span>}
    </span>
  )
}

function Result({ outcome }: { outcome: Outcome }) {
  const t = useText()
  const tone = outcome === "ok" ? "text-good" : outcome === "empty" ? "text-warn" : "text-alarm"

  return <span className={tone}>{t(outcomeWords[outcome])}</span>
}

function Share({ value, tone }: { value: number; tone: string }) {
  const language = useLanguage()

  return <span className={value > 0 ? tone : "text-muted"}>{value.toLocaleString(language)}</span>
}

function look(t: Text, verdict: Verdict, way: string): [string, string] {
  switch (verdict) {
    case "out":
      return [way, "bg-chip text-chip-ink"]
    case "block":
      return [t("access.block"), "bg-alarm-soft text-alarm"]
    case "held":
      return [t("access.held"), "bg-warn-soft text-warn"]
    case "guard":
      return [t("access.guard"), "bg-alarm-soft text-alarm"]
    default:
      return [t("access.direct"), "bg-hover text-muted"]
  }
}

function aside(t: Text, verdict: Verdict, way: string, path: string): string {
  if (verdict === "held") {
    return way
  }

  if (verdict === "guard") {
    return way.toUpperCase()
  }

  if (verdict !== "out" || path === "") {
    return ""
  }

  return t(path === "relay" ? "access.relay" : "access.local")
}

function filterOf(view: View, at: number): AccessFilter {
  const way = view.how.startsWith("way:") ? view.how.slice(4) : ""
  const [from, to] =
    view.period === "range"
      ? [instant(view.since), instant(view.until)]
      : [new Date(at - hours[view.period] * 3600000).toISOString(), new Date(at).toISOString()]

  return {
    from,
    to,
    client: view.client,
    verdict: way.length > 0 ? "out" : stops.some((one) => one === view.how) ? view.how : "",
    way,
    path: view.how === "local" || view.how === "relay" ? view.how : "",
    outcome: view.outcome,
    search: view.search.trim(),
  }
}

function instant(text: string): string {
  const at = new Date(text)

  return text.length === 0 || Number.isNaN(at.getTime()) ? "" : at.toISOString()
}

function local(at: Date): string {
  return new Date(at.getTime() - at.getTimezoneOffset() * 60000).toISOString().slice(0, 16)
}

function counted(counts: AccessCount[], key: string): number {
  return counts.find((one) => one.key === key)?.count ?? 0
}

function plural(language: string, words: Words, count: number): TextKey {
  return words[new Intl.PluralRules(language).select(count)]
}

function moment(time: string, language: string): string {
  const at = new Date(time)
  if (at.toDateString() === new Date().toDateString()) {
    return at.toLocaleTimeString(language)
  }

  return at.toLocaleString(language, {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  })
}

function day(time: string, language: string): string {
  return new Date(time).toLocaleString(language, { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" })
}

function spot(record: AccessRecord): string {
  if (record.port === 0) {
    return record.target
  }

  return record.target.includes(":") ? `[${record.target}]:${record.port}` : `${record.target}:${record.port}`
}

function protocolName(protocol: number): string {
  switch (protocol) {
    case 1:
      return "ICMP"
    case 6:
      return "TCP"
    case 17:
      return "UDP"
    case 58:
      return "ICMPv6"
    default:
      return `IP ${protocol}`
  }
}

function address(key: string): boolean {
  return key.includes(":") || /^\d{1,3}(\.\d{1,3}){3}$/.test(key)
}
