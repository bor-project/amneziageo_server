import { useState } from "react"
import type { MouseEvent, ReactNode } from "react"
import { Link } from "react-router-dom"
import { useResolver } from "@/api/dns"
import type { DnsFinding, DnsLevel, DnsSubject } from "@/api/dns"
import { levelOf, ofTemplate, tell, tone } from "@/components/dnsWords"
import type { DnsContext, DnsView, Said } from "@/components/dnsWords"
import { menu } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"

const most = 10

const width = 440

// The icon of a level with the list of what the findings say behind it.
export function DnsMark({
  findings,
  view,
  title,
  everyone,
  members,
}: {
  findings: DnsFinding[]
  view: DnsView
  title?: string
  everyone?: (subject: DnsSubject) => boolean
  members?: number[]
}) {
  const t = useText()
  const language = useLanguage()
  const held = useDnsContext()
  const context = everyone === undefined ? held : { ...held, everyone }
  const shown = members === undefined ? findings : ofTemplate(findings, members)
  const level = levelOf(shown)
  const [spot, setSpot] = useState<{ top: number; left: number } | null>(null)

  if (level === null) {
    return null
  }

  const said = shown.filter((one) => one.level !== "hint").map((one) => tell(t, language, one, view, context))
  const hover = title ?? said.map((one) => one.text).join("\n")

  function toggle(event: MouseEvent<HTMLButtonElement>) {
    event.preventDefault()
    event.stopPropagation()
    if (spot !== null) {
      setSpot(null)

      return
    }

    const box = event.currentTarget.getBoundingClientRect()
    setSpot({ top: box.bottom + 4, left: Math.max(8, Math.min(box.left, window.innerWidth - width - 8)) })
  }

  return (
    <span className="inline-flex shrink-0">
      <button type="button" title={hover} aria-label={hover} onClick={toggle} className="flex items-center">
        <LevelIcon level={level} />
      </button>
      {spot !== null && (
        <>
          <span className="fixed inset-0 z-30" onMouseDown={() => setSpot(null)} />
          <span
            style={{ top: spot.top, left: spot.left, maxWidth: width }}
            className={`fixed z-40 flex w-max flex-col gap-1 px-3 py-2.5 text-left text-[13px] font-normal whitespace-normal text-body ${menu}`}
          >
            {title !== undefined && <span className="font-medium text-ink">{title}</span>}
            <Told said={said} />
          </span>
        </>
      )}
    </span>
  )
}

// The findings of a draft as lines of a form, each with its list behind the icon.
export function DnsNotes({
  findings,
  view,
  skip = [],
  members,
}: {
  findings: DnsFinding[]
  view: DnsView
  skip?: string[]
  members?: number[]
}) {
  const t = useText()
  const language = useLanguage()
  const context = { ...useDnsContext(), draft: true }
  const shown = (members === undefined ? findings : ofTemplate(findings, members)).filter((one) => !skip.includes(one.code))

  if (shown.length === 0) {
    return null
  }

  return (
    <div className="flex flex-col gap-1 sm:col-span-2">
      {shown.map((one) => {
        const said = tell(t, language, one, view, context)

        return (
          <div key={said.key} className={`flex items-start gap-1.5 text-sm ${tone(one.level)}`}>
            {one.level !== "hint" && (
              <span className="mt-0.5">
                {said.rows.length > 0 ? <DnsMark findings={[one]} view={view} /> : <LevelIcon level={one.level} />}
              </span>
            )}
            <span>{said.text}</span>
          </div>
        )
      })}
    </div>
  )
}

export function LevelIcon({ level }: { level: DnsLevel }) {
  if (level === "error") {
    return (
      <svg viewBox="0 0 24 24" className="size-4 text-alarm" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden>
        <circle cx="12" cy="12" r="9" />
        <path d="M12 7.5v5.5M12 16.5h.01" strokeLinecap="round" />
      </svg>
    )
  }

  return (
    <svg
      viewBox="0 0 24 24"
      className={`size-4 ${level === "warning" ? "text-warn" : "text-muted"}`}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      aria-hidden
    >
      <path d="M10.3 4.2 2.8 17.5A2 2 0 0 0 4.5 20.5h15a2 2 0 0 0 1.7-3L13.7 4.2a2 2 0 0 0-3.4 0Z" strokeLinejoin="round" />
      <path d="M12 9.5v4M12 17h.01" strokeLinecap="round" />
    </svg>
  )
}

function Told({ said }: { said: Said[] }) {
  const t = useText()
  const lines: ReactNode[] = []
  let left = 0

  for (const one of said) {
    if (lines.length >= most) {
      left += 1 + one.rows.length
      continue
    }

    if (one.text.length > 0) {
      lines.push(
        <span key={one.key} className={one.rows.length > 0 ? `font-medium ${tone(one.level)}` : tone(one.level)}>
          {one.text}
        </span>,
      )
    }

    for (const row of one.rows) {
      if (lines.length >= most) {
        left++
        continue
      }

      lines.push(
        <span key={`${one.key}-${row.key}`}>
          <Link to={`/clients/${row.id}/settings`} className="text-brand-ink hover:text-brand-lit">
            {row.name}
          </Link>
          {row.how.length > 0 && ` - ${row.how}`}
        </span>,
      )
    }
  }

  return (
    <>
      {lines}
      {left > 0 && <span className="text-muted">{t("dnsCheck.more", { count: left })}</span>}
    </>
  )
}

function useDnsContext(): DnsContext {
  const resolver = useResolver(false).data

  return { port: resolver?.port ?? 53, draft: false, everyone: () => false }
}
