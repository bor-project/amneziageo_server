import { useEffect, useRef, useState } from "react"
import axios from "axios"
import { complaint } from "@/api/auth"
import { useBackup, useRestore } from "@/api/backup"
import { useFailure } from "@/api/failure"
import { useHealth } from "@/api/health"
import { useOverview, useServices } from "@/api/overview"
import type { Overview, ServiceFault, ServiceHealth } from "@/api/overview"
import { scopes } from "@/api/scopes"
import { busy, useApplyUpdate, useCheckUpdate, useUpdate } from "@/api/update"
import { Sparkline } from "@/components/Chart"
import type { Trace } from "@/components/Chart"
import { Dialog } from "@/components/Dialog"
import { useCrumbs } from "@/components/crumbs"
import { Help } from "@/components/fields"
import { card, danger, quiet, secondary, tool } from "@/components/styles"
import { average, bytes, peak, percent, rate, share, span } from "@/format"
import { useLanguage, useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppDispatch, useAppSelector } from "@/store/hooks"
import { updateWatched } from "@/store/uiSlice"

const small = "rounded-md bg-brand px-2.5 py-1 text-xs font-medium text-white hover:bg-brand-hover disabled:opacity-50"

const serviceTitles: Record<string, TextKey> = {
  subscription: "services.subscription",
  endpoint: "services.endpoint",
  dns: "services.dns",
  certificate: "services.certificate",
}

const serviceParts: Record<string, TextKey> = {
  hello: "services.partHello",
  speed: "services.partSpeed",
  websocket: "services.partWebSocket",
  subscription: "services.partSubscription",
}

const serviceFaults: Record<string, TextKey> = {
  "no-host": "services.noHost",
  "interface-down": "services.interfaceDown",
  "port-refused": "services.portRefused",
  "port-closed": "services.portClosed",
  "front-down": "services.frontDown",
  "front-fell": "services.frontFell",
  "front-deaf": "services.frontDeaf",
  "no-endpoint": "services.noEndpoint",
  "resolver-down": "services.resolverDown",
  "resolver-way": "services.resolverWay",
  "certificate-unreadable": "services.certificateUnreadable",
}

const serviceRow = "grid grid-cols-[14px_240px_minmax(0,1fr)] items-baseline gap-x-2.5 gap-y-1 py-2 text-sm max-sm:grid-cols-[14px_minmax(0,1fr)]"

const serviceLine = "col-start-3 max-sm:col-start-2"

export function Dashboard() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const health = useHealth()
  const overview = useOverview()
  const data = overview.data
  const failed = useFailure(overview)
  const keeps = holds(user, scopes.readBackup) || holds(user, scopes.manageAccess)

  useCrumbs([{ label: t("nav.overview") }])

  if (!data) {
    return (
      <div className="flex flex-col gap-4">
        <h1 className="text-2xl leading-10 font-semibold tracking-[-0.02em]">{t("nav.overview")}</h1>
        {failed !== null ? (
          <div className="text-sm text-alarm">{t(complaint(failed))}</div>
        ) : (
          <div className="text-sm text-muted">{t("page.loading")}</div>
        )}
      </div>
    )
  }

  const window = data.window
  const memory = window.memory.map((one) => share(one, data.memory.total))
  const swap = window.swap.map((one) => share(one, data.swap.total))
  const storage = window.storage.map((one) => share(one, data.storage.total))

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl leading-10 font-semibold tracking-[-0.02em]">{t("nav.overview")}</h1>
      {failed !== null && <div className="text-sm text-alarm">{t(complaint(failed))}</div>}

      <Head data={data} version={health.data?.version} />

      <Services />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Meter
          icon={<Chip />}
          title={t("overview.cpu")}
          value={data.cpu}
          note={t("overview.processor", {
            cores: data.processor.cores,
            threads: data.processor.threads,
            clock: (data.processor.megahertz / 1000).toFixed(2),
          })}
          values={window.cpu}
          left={t("overview.avg", { value: percent(average(window.cpu)) })}
          right={t("overview.peak", { value: percent(peak(window.cpu)) })}
        />

        <Meter
          icon={<Board />}
          title={t("overview.memory")}
          value={share(data.memory.used, data.memory.total)}
          note={`${bytes(t, data.memory.used)} / ${bytes(t, data.memory.total)}`}
          values={memory}
          left={t("overview.avg", { value: percent(average(memory)) })}
          right={t("overview.peak", { value: percent(peak(memory)) })}
        />

        <Meter
          icon={<Arrows />}
          title={t("overview.swap")}
          value={share(data.swap.used, data.swap.total)}
          note={`${bytes(t, data.swap.used)} / ${bytes(t, data.swap.total)}`}
          values={swap}
          left={t("overview.avg", { value: percent(average(swap)) })}
          right={t("overview.peak", { value: percent(peak(swap)) })}
        />

        <Meter
          icon={<Disk />}
          title={t("overview.storage")}
          value={share(data.storage.used, data.storage.total)}
          note={`${bytes(t, data.storage.used)} / ${bytes(t, data.storage.total)}`}
          values={storage}
          left={t("overview.free", { value: bytes(t, data.storage.total - data.storage.used) })}
          right={t("overview.avg", { value: percent(average(storage)) })}
        />
      </div>

      <div className="grid gap-4 xl:grid-cols-3">
        <div className={`p-4 xl:col-span-2 ${card}`}>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <div className="text-sm font-medium text-ink">{t("overview.speed")}</div>
              <div className="text-xs text-muted">
                {t("overview.speedPeak", {
                  value: rate(t, Math.max(peak(window.upload), peak(window.download))),
                })}
              </div>
            </div>
            <div className="flex gap-5">
              <Legend tone="bg-brand" title={t("overview.upload")} value={rate(t, data.traffic.upload)} />
              <Legend tone="bg-muted" title={t("overview.download")} value={rate(t, data.traffic.download)} />
            </div>
          </div>

          <div className="mt-4">
            <Sparkline
              height="h-44"
              traces={[
                { values: window.upload, tone: "text-brand" },
                { values: window.download, tone: "text-muted" },
              ]}
            />
          </div>

          <div className="mt-4 grid grid-cols-2 gap-4 border-t border-line pt-4 sm:grid-cols-3">
            <Fact title={t("overview.sent")} value={bytes(t, data.traffic.sent)} />
            <Fact title={t("overview.received")} value={bytes(t, data.traffic.received)} />
            <Fact
              title={t("overview.window")}
              value={`${rate(t, average(window.upload))} / ${rate(t, average(window.download))}`}
            />
          </div>
        </div>

        <div className={`p-4 ${card}`}>
          <div className="text-sm font-medium text-ink">{t("overview.connections")}</div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-3xl font-semibold text-ink">{data.sockets.tcp + data.sockets.udp}</span>
            <span className="text-sm text-muted">{t("overview.sockets")}</span>
          </div>

          <div className="mt-3 flex gap-5">
            <Legend tone="bg-brand" title={t("overview.tcp")} value={String(data.sockets.tcp)} />
            <Legend tone="bg-muted" title={t("overview.udp")} value={String(data.sockets.udp)} />
          </div>

          <div className="mt-4">
            <Sparkline height="h-44" traces={[{ values: window.sockets, tone: "text-brand" }]} />
          </div>
        </div>
      </div>

      <div className={`grid gap-4 ${keeps ? "xl:grid-cols-3" : ""}`}>
        <div className={`grid gap-6 p-4 md:grid-cols-3 ${keeps ? "xl:col-span-2" : ""} ${card}`}>
          <Group title={t("overview.uptime")}>
            <Fact title={t("overview.host")} value={span(t, data.hostUptime)} />
            <Fact title={t("overview.panel")} value={span(t, data.panelUptime)} />
          </Group>

          <Group title={t("overview.process")}>
            <Fact title={t("overview.ram")} value={bytes(t, data.panelMemory)} />
            <Fact title={t("overview.threads")} value={String(data.panelThreads)} />
          </Group>

          <Addresses list={data.addresses} />
        </div>

        {keeps && <Backup />}
      </div>
    </div>
  )
}

// Takes a copy of the database of the panel and puts one back.
function Backup() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const backup = useBackup()
  const restore = useRestore()
  const picker = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [share, setShare] = useState(0)
  const take = holds(user, scopes.readBackup)
  const put = holds(user, scopes.manageAccess)

  async function go(picked: File) {
    setFile(null)
    setShare(0)
    try {
      await restore.mutateAsync({ file: picked, progress: setShare })
      window.location.reload()
    } catch {
      return
    }
  }

  return (
    <div className={`flex flex-col gap-3 p-4 ${card}`}>
      <div className="flex items-center gap-2 text-sm font-medium text-ink">
        <Box />
        {t("backup.title")}
        <Help text={t("backup.about")} />
      </div>

      <div className="mt-auto flex flex-wrap gap-2">
        {take && (
          <button
            type="button"
            className={tool}
            disabled={backup.isPending}
            onClick={() => backup.mutate()}
          >
            <Down />
            {t("backup.download")}
          </button>
        )}
        {put && (
          <button
            type="button"
            className={tool}
            disabled={restore.isPending}
            onClick={() => picker.current?.click()}
          >
            <Up />
            {t("backup.restore")}
          </button>
        )}
        <input
          ref={picker}
          type="file"
          accept=".db,.sqlite,.sqlite3,application/vnd.sqlite3,application/octet-stream"
          className="hidden"
          onChange={(event) => {
            const picked = event.target.files?.[0]
            event.target.value = ""
            if (picked !== undefined) {
              restore.reset()
              setFile(picked)
            }
          }}
        />
      </div>

      {restore.isPending && (
        <div className="flex flex-col gap-1.5">
          <div className="h-1.5 overflow-hidden rounded-full bg-line">
            <div
              className={`h-full rounded-full bg-brand ${share >= 1 ? "animate-pulse" : ""}`}
              style={{ width: `${Math.round(Math.max(share, 0.03) * 100)}%` }}
            />
          </div>
          <div className="text-xs text-muted">
            {share >= 1 ? t("backup.restarting") : t("backup.sending", { share: Math.round(share * 100) })}
          </div>
        </div>
      )}

      {backup.isError && <div className="text-sm text-alarm">{t(complaint(backup.error))}</div>}
      {restore.isError && (
        <div className="text-sm text-alarm">
          {t(complaint(restore.error))}
          {told(restore.error).length > 0 && <div className="mt-0.5 text-xs text-muted">{told(restore.error)}</div>}
        </div>
      )}

      {file !== null && (
        <Dialog
          title={t("backup.confirmTitle")}
          onClose={() => setFile(null)}
          actions={
            <>
              <button type="button" className={secondary} onClick={() => setFile(null)}>
                {t("backup.cancel")}
              </button>
              <button type="button" className={danger} onClick={() => void go(file)}>
                {t("backup.confirm")}
              </button>
            </>
          }
        >
          <p>{t("backup.confirmFile", { name: file.name, size: bytes(t, file.size) })}</p>
        </Dialog>
      )}
    </div>
  )
}

// Returns what the panel said about a refused backup.
function told(error: unknown): string {
  if (!axios.isAxiosError(error)) {
    return ""
  }

  const said = (error.response?.data as { error?: string; message?: string } | undefined) ?? {}

  return said.error === "backup-elsewhere" ? (said.message ?? "") : ""
}

function Head({ data, version }: { data: Overview; version?: string }) {
  const t = useText()
  const seen = useRef<string | undefined>(undefined)

  useEffect(() => {
    if (!version || seen.current === version) {
      return
    }

    if (seen.current === undefined) {
      seen.current = version
      return
    }

    const timer = window.setTimeout(() => window.location.reload(), 2000)

    return () => window.clearTimeout(timer)
  }, [version])

  return (
    <div className={`px-4 py-3 ${card}`}>
      <div className="flex flex-wrap items-center gap-3">
        <span className={`size-2 rounded-full ${data.tunnel.loaded ? "bg-good" : "bg-alarm"}`} />
        <span className="text-sm font-medium text-ink">{t("overview.kernel")}</span>
        <span className="text-sm text-muted">
          {data.tunnel.loaded ? t("overview.loaded", { version: data.tunnel.version }) : t("overview.missing")}
        </span>
        <span className="text-sm text-muted">{t("overview.interfaces", { count: data.tunnel.interfaces })}</span>
        <div className="ml-auto flex flex-wrap items-center gap-3 text-sm text-muted">
          {version && <span>{t("overview.version", { version })}</span>}
          <Update />
        </div>
      </div>

      <Failed />
    </div>
  )
}

function Services() {
  const t = useText()
  const language = useLanguage()
  const report = useServices(true).data

  if (!report || report.services.length === 0) {
    return null
  }

  return (
    <div className={`px-4 py-3 ${card}`}>
      <div className="flex items-center justify-between gap-3">
        <span className="text-sm font-medium text-ink">{t("services.title")}</span>
        {report.checked && (
          <span className="text-xs text-faint">
            {t("services.checked", { time: new Date(report.checked).toLocaleTimeString(language) })}
          </span>
        )}
      </div>

      <div className="mt-1 divide-y divide-line">
        {report.services.map((one) => (
          <Service key={`${one.kind}:${one.name}`} one={one} />
        ))}
      </div>
    </div>
  )
}

function Service({ one }: { one: ServiceHealth }) {
  const t = useText()
  const language = useLanguage()
  const down = one.faults.length > 0
  const tone = down ? "bg-alarm" : one.notes.length > 0 ? "bg-warn" : "bg-good"
  const named = serviceTitles[one.kind]
  const title = named ? t(named, { name: one.name }) : one.kind
  const facts = serviceFacts(one, t, language)

  return (
    <div className={serviceRow}>
      <span className={`size-2 self-center rounded-full ${tone}`} />
      <span className="truncate text-ink" title={title}>
        {title}
      </span>
      {facts !== "" && <span className={`text-muted ${serviceLine}`}>{facts}</span>}
      {down && <span className={`text-alarm ${serviceLine}`}>{serviceDown(one, t, language)}</span>}
      {!down &&
        one.notes.map((note) => (
          <span key={note.code} className={`text-warn ${serviceLine}`}>
            {serviceNote(note, t)}
          </span>
        ))}
    </div>
  )
}

function serviceFacts(one: ServiceHealth, t: Text, language: string): string {
  if (one.kind === "certificate") {
    return one.until !== null && one.faults.length === 0 ? t("services.until", { date: day(one.until, language) }) : ""
  }

  const ports = one.ports.map((port) => `${port.protocol} ${port.port}`)
  const place = ports.length > 0 ? ports.join(", ") : one.kind === "subscription" ? t("services.onEndpoints") : ""
  const parts = one.parts.map((part) => {
    const word = serviceParts[part]

    return word ? t(word) : part
  })

  return [place, parts.join(", ")].filter((piece) => piece !== "").join(" · ")
}

function serviceDown(one: ServiceHealth, t: Text, language: string): string {
  if (one.kind === "certificate") {
    return one.faults
      .map((fault) =>
        fault.code === "certificate-expired" && one.until !== null
          ? t("services.certificateExpired", { date: day(one.until, language) })
          : serviceFault(fault, t),
      )
      .join("; ")
  }

  return t("services.down", {
    time: moment(one.since, language),
    why: one.faults.map((fault) => serviceFault(fault, t)).join("; "),
  })
}

function serviceFault(fault: ServiceFault, t: Text): string {
  if (fault.code === "interface-down" && fault.detail === "no-module") {
    return t("services.noModule")
  }

  const word = serviceFaults[fault.code]
  if (!word) {
    return fault.detail !== "" ? `${fault.code} (${fault.detail})` : fault.code
  }

  const text = t(word, { detail: fault.detail })

  return fault.detail !== "" && !text.includes(fault.detail) ? `${text} (${fault.detail})` : text
}

function serviceNote(note: ServiceFault, t: Text): string {
  if (note.code !== "certificate-expiring") {
    return note.detail !== "" ? `${note.code} (${note.detail})` : note.code
  }

  return note.detail === "0" ? t("services.certificateToday") : t("services.certificateExpiring", { days: note.detail })
}

function day(value: string, language: string): string {
  return new Date(value).toLocaleDateString(language)
}

function moment(value: string, language: string): string {
  const at = new Date(value)
  const clock = at.toLocaleTimeString(language, { hour: "2-digit", minute: "2-digit" })

  return at.toDateString() === new Date().toDateString() ? clock : `${at.toLocaleDateString(language)} ${clock}`
}

function Update() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const may = holds(user, scopes.manageUpdates)
  const update = useUpdate()
  const check = useCheckUpdate()
  const apply = useApplyUpdate()
  const dispatch = useAppDispatch()
  const [fault, setFault] = useState<TextKey | null>(null)
  const state = update.data

  if (!state) {
    return null
  }

  if (busy(state)) {
    return (
      <span className="flex items-center gap-2 text-ink">
        <span className="size-2 animate-pulse rounded-full bg-brand" />
        {t("update.running", { version: state.run?.to ?? state.latest?.version ?? "" })}
      </span>
    )
  }

  const latest = state.latest
  const why =
    fault === "error.updateBlocked" && state.blocker !== "" ? t(`update.blocker.${state.blocker}` as TextKey) : undefined

  async function go(version: string) {
    setFault(null)
    try {
      const moved = await apply.mutateAsync(version)
      dispatch(updateWatched({ to: moved.latest?.version ?? version, started: Date.now() }))
    } catch (error) {
      setFault(complaint(error))
    }
  }

  return (
    <>
      {latest && (
        <a
          href={latest.notes.length > 0 ? latest.notes : undefined}
          target="_blank"
          rel="noreferrer"
          className="text-brand hover:underline"
        >
          {t("update.available", { version: latest.version })}
        </a>
      )}

      {latest && may && (
        <button type="button" className={small} disabled={apply.isPending} onClick={() => void go(latest.version)}>
          {t("update.confirm", { version: latest.version })}
        </button>
      )}

      {fault !== null && (
        <span className="text-alarm" title={why}>
          {t(fault)}
        </span>
      )}

      {check.isError && <span className="text-alarm">{t(complaint(check.error))}</span>}

      {state.fault && (
        <span className="text-alarm" title={state.fault.message}>
          {t("update.fault")}
        </span>
      )}

      {may && (
        <button
          type="button"
          className={quiet}
          title={t("update.check")}
          aria-label={t("update.check")}
          disabled={check.isPending}
          onClick={() => check.mutate()}
        >
          <Again spinning={check.isPending} />
        </button>
      )}
    </>
  )
}

function Failed() {
  const t = useText()
  const update = useUpdate()
  const run = update.data?.run

  if (!run || run.state !== "failed") {
    return null
  }

  return (
    <details className="mt-3 border-t border-line pt-3 text-sm">
      <summary className="cursor-pointer text-alarm">{t("update.failed", { version: run.to })}</summary>
      <pre className="mt-2 max-h-64 overflow-auto text-xs whitespace-pre-wrap text-muted">{run.log.join("\n")}</pre>
    </details>
  )
}

function Meter({
  icon,
  title,
  value,
  note,
  values,
  left,
  right,
}: {
  icon: React.ReactNode
  title: string
  value: number
  note: string
  values: number[]
  left: string
  right: string
}) {
  const traces: Trace[] = [{ values, tone: value >= 90 ? "text-alarm" : "text-brand" }]

  return (
    <div className={`p-4 ${card}`}>
      <div className="flex items-center gap-2 text-sm text-muted">
        {icon}
        {title}
      </div>

      <div className="mt-2 text-3xl font-semibold text-ink">{percent(value)}</div>
      <div className="text-xs text-faint">{note}</div>

      <div className="mt-3">
        <Sparkline traces={traces} mean />
      </div>

      <div className="mt-2 flex justify-between text-[11px] tracking-wide text-faint">
        <span>{left}</span>
        <span>{right}</span>
      </div>
    </div>
  )
}

function Legend({ tone, title, value }: { tone: string; title: string; value: string }) {
  return (
    <div className="flex items-center gap-2">
      <span className={`size-2 rounded-full ${tone}`} />
      <span className="text-xs text-muted">{title}</span>
      <span className="text-sm font-medium text-ink">{value}</span>
    </div>
  )
}

function Group({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="text-[11px] tracking-wide text-faint uppercase">{title}</div>
      <div className="mt-2 grid grid-cols-2 gap-4">{children}</div>
    </div>
  )
}

function Fact({ title, value }: { title: string; value: string }) {
  return (
    <div>
      <div className="text-[11px] tracking-wide text-faint uppercase">{title}</div>
      <div className="mt-1 text-sm font-medium text-ink">{value}</div>
    </div>
  )
}

function Addresses({ list }: { list: string[] }) {
  const t = useText()
  const [shown, setShown] = useState(false)

  return (
    <div>
      <div className="flex items-center justify-between">
        <div className="text-[11px] tracking-wide text-faint uppercase">{t("overview.addresses")}</div>
        <button type="button" onClick={() => setShown(!shown)} className={quiet}>
          <Eye open={shown} />
        </button>
      </div>

      <div className={`mt-2 space-y-1 text-sm text-ink ${shown ? "" : "blur-sm select-none"}`}>
        {list.map((one) => (
          <div key={one}>{one}</div>
        ))}
      </div>
    </div>
  )
}

function Chip() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.6">
      <rect x="7" y="7" width="10" height="10" rx="1.5" />
      <path d="M10 3v3M14 3v3M10 18v3M14 18v3M3 10h3M3 14h3M18 10h3M18 14h3" strokeLinecap="round" />
    </svg>
  )
}

function Board() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.6">
      <rect x="3" y="7" width="18" height="10" rx="1.5" />
      <path d="M7 11v3M11 11v3M15 11v3M19 11v3" strokeLinecap="round" />
    </svg>
  )
}

function Arrows() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d="M4 8h13l-3-3M20 16H7l3 3" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

function Disk() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.6">
      <ellipse cx="12" cy="6" rx="8" ry="3" />
      <path d="M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6" />
      <path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3" />
    </svg>
  )
}

function Down() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M12 4v11M7.5 10.5 12 15l4.5-4.5M5 19.5h14" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

function Up() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M12 15V4M7.5 8.5 12 4l4.5 4.5M5 19.5h14" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

function Box() {
  return (
    <svg viewBox="0 0 24 24" className="size-4 text-muted" fill="none" stroke="currentColor" strokeWidth="1.6">
      <rect x="3.5" y="4" width="17" height="5" rx="1.2" />
      <path d="M5 9v9.5a1.5 1.5 0 0 0 1.5 1.5h11a1.5 1.5 0 0 0 1.5-1.5V9M10 13h4" strokeLinecap="round" />
    </svg>
  )
}

function Again({ spinning }: { spinning: boolean }) {
  return (
    <svg
      viewBox="0 0 24 24"
      className={`size-4 ${spinning ? "animate-spin" : ""}`}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
    >
      <path d="M20 12a8 8 0 1 1-2.34-5.66M20 4v4h-4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

function Eye({ open }: { open: boolean }) {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d="M2 12s3.6-6 10-6 10 6 10 6-3.6 6-10 6-10-6-10-6Z" strokeLinejoin="round" />
      <circle cx="12" cy="12" r="2.5" />
      {!open && <path d="m4 4 16 16" strokeLinecap="round" />}
    </svg>
  )
}
