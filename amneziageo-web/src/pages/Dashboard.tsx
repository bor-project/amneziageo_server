import { useState } from "react"
import { useHealth } from "@/api/health"
import { useOverview } from "@/api/overview"
import type { Overview } from "@/api/overview"
import { Sparkline } from "@/components/Chart"
import type { Trace } from "@/components/Chart"
import { useCrumbs } from "@/components/crumbs"
import { card, quiet } from "@/components/styles"
import { average, bytes, peak, percent, rate, share, span } from "@/format"
import { useText } from "@/i18n"

export function Dashboard() {
  const t = useText()
  const health = useHealth()
  const overview = useOverview()
  const data = overview.data

  useCrumbs([{ label: t("nav.overview") }])

  if (!data) {
    return <div />
  }

  const window = data.window
  const memory = window.memory.map((one) => share(one, data.memory.total))
  const swap = window.swap.map((one) => share(one, data.swap.total))
  const storage = window.storage.map((one) => share(one, data.storage.total))

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl leading-10 font-semibold tracking-[-0.02em]">{t("nav.overview")}</h1>

      <Head data={data} version={health.data?.version} />

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

      <div className={`grid gap-6 p-4 md:grid-cols-3 ${card}`}>
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
    </div>
  )
}

function Head({ data, version }: { data: Overview; version?: string }) {
  const t = useText()

  return (
    <div className={`flex flex-wrap items-center gap-3 px-4 py-3 ${card}`}>
      <span className={`size-2 rounded-full ${data.tunnel.loaded ? "bg-good" : "bg-alarm"}`} />
      <span className="text-sm font-medium text-ink">{t("overview.kernel")}</span>
      <span className="text-sm text-muted">
        {data.tunnel.loaded ? t("overview.loaded", { version: data.tunnel.version }) : t("overview.missing")}
      </span>
      <span className="text-sm text-muted">{t("overview.interfaces", { count: data.tunnel.interfaces })}</span>
      {version && <span className="ml-auto text-sm text-muted">{t("overview.version", { version })}</span>}
    </div>
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

function Eye({ open }: { open: boolean }) {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d="M2 12s3.6-6 10-6 10 6 10 6-3.6 6-10 6-10-6-10-6Z" strokeLinejoin="round" />
      <circle cx="12" cy="12" r="2.5" />
      {!open && <path d="m4 4 16 16" strokeLinecap="round" />}
    </svg>
  )
}
