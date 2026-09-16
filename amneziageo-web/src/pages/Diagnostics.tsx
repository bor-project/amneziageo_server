import { Fragment } from "react"
import { useJournal, useVersions } from "@/api/diagnostics"
import type { JournalEntry } from "@/api/diagnostics"
import { useHealth } from "@/api/health"
import { useOverview } from "@/api/overview"
import { SortCaption } from "@/components/Rows"
import { SortControl } from "@/components/SortControl"
import { ariaSort, useOrder, useSorted } from "@/components/sort"
import { card, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import { useAbove, wideQuery } from "@/theme/width"

const loud = new Set(["Warning", "Error", "Critical"])
const levels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"]

export function Diagnostics() {
  const t = useText()
  const language = useLanguage()
  const health = useHealth()
  const overview = useOverview()
  const versions = useVersions()
  const journal = useJournal()
  const roomy = useAbove(wideQuery)
  const tunnel = overview.data?.tunnel
  const known = versions.data
  const { order, toggle, choose, direct } = useOrder()

  const heads = [
    { key: "time", caption: t("diagnostics.time"), pad: "px-4", sort: (entry: JournalEntry) => Date.parse(entry.time) },
    {
      key: "level",
      caption: t("diagnostics.level"),
      pad: "px-2",
      sort: (entry: JournalEntry) => (levels.includes(entry.level) ? levels.indexOf(entry.level) : null),
    },
    {
      key: "source",
      caption: t("diagnostics.source"),
      pad: "px-2",
      sort: (entry: JournalEntry) => entry.category.split(".").at(-1) ?? entry.category,
    },
    { key: "message", caption: t("diagnostics.message"), pad: "px-4", sort: (entry: JournalEntry) => entry.message },
  ]

  const chosen = heads.find((head) => head.key === order?.key)
  const entries = useSorted(journal.data ?? [], chosen?.sort, order)

  const rows: [string, string][] = [
    [t("diagnostics.panel"), health.data?.version ?? ""],
    [t("diagnostics.amneziawg"), tunnel === undefined ? "" : tunnel.loaded ? tunnel.version : t("diagnostics.missing")],
    [t("diagnostics.runtime"), known?.runtime ?? ""],
    [t("diagnostics.system"), known?.distribution ?? ""],
    [t("diagnostics.kernel"), known?.kernel ?? ""],
    [t("diagnostics.wstunnel"), known === undefined ? "" : known.wstunnel.length > 0 ? known.wstunnel : t("diagnostics.missing")],
  ]

  return (
    <div className="mt-4 flex flex-col gap-4">
      <div className={`px-4 py-3 ${card}`}>
        <div className="text-sm font-semibold text-ink-soft">{t("diagnostics.versions")}</div>
        <div className="mt-2 grid grid-cols-[max-content_1fr] gap-x-6 gap-y-1 text-sm">
          {rows.map(([caption, value]) => (
            <Fragment key={caption}>
              <span className="text-faint">{caption}</span>
              <span className="text-body">{value}</span>
            </Fragment>
          ))}
        </div>
      </div>

      <div className={card}>
        <div className="flex items-center justify-between border-b border-line px-4 py-3">
          <span className="text-sm font-semibold text-ink-soft">{t("diagnostics.log")}</span>
          <button type="button" onClick={() => void journal.refetch()} className={secondary}>
            {t("diagnostics.refresh")}
          </button>
        </div>

        {journal.data?.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("diagnostics.empty")}</div>}

        {journal.data && journal.data.length > 0 && (
          <div className="max-h-[60vh] overflow-auto">
            {roomy ? (
              <table className="w-full text-left text-xs">
                <thead className="text-faint">
                  <tr>
                    {heads.map((head) => (
                      <th
                        key={head.key}
                        aria-sort={ariaSort(head.key, order)}
                        className={`${head.pad} py-1.5 font-normal whitespace-nowrap`}
                      >
                        <SortCaption caption={head.caption} name={head.key} order={order} toggle={toggle} />
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {entries.map((place) => (
                    <Record key={place.at} entry={place.item} language={language} />
                  ))}
                </tbody>
              </table>
            ) : (
              <>
                <div className="border-b border-line px-4 py-3">
                  <SortControl options={heads} order={order} choose={choose} direct={direct} fill />
                </div>
                {entries.map((place) => (
                  <Note key={place.at} entry={place.item} language={language} />
                ))}
              </>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

function Note({ entry, language }: { entry: JournalEntry; language: string }) {
  const tone = loud.has(entry.level) ? "text-alarm" : "text-muted"
  const source = entry.category.split(".").at(-1) ?? entry.category

  return (
    <div className="border-t border-line-soft px-4 py-2 text-xs">
      <div className="flex flex-wrap items-center gap-2 text-muted">
        <span>{new Date(entry.time).toLocaleString(language)}</span>
        <span className={tone}>{entry.level}</span>
        <span title={entry.category}>{source}</span>
      </div>
      <div className="mt-1 break-all text-body">
        {entry.message}
        {entry.fault.length > 0 && <div className="text-alarm">{entry.fault}</div>}
      </div>
    </div>
  )
}

function Record({ entry, language }: { entry: JournalEntry; language: string }) {
  const tone = loud.has(entry.level) ? "text-alarm" : "text-muted"
  const source = entry.category.split(".").at(-1) ?? entry.category

  return (
    <tr className="border-t border-line-soft align-top hover:bg-hover">
      <td className="px-4 py-1.5 whitespace-nowrap text-muted">{new Date(entry.time).toLocaleString(language)}</td>
      <td className={`px-2 py-1.5 whitespace-nowrap ${tone}`}>{entry.level}</td>
      <td className="px-2 py-1.5 whitespace-nowrap text-muted" title={entry.category}>
        {source}
      </td>
      <td className="px-4 py-1.5 break-all text-body">
        {entry.message}
        {entry.fault.length > 0 && <div className="text-alarm">{entry.fault}</div>}
      </td>
    </tr>
  )
}
