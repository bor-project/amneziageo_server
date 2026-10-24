import type { ReactNode } from "react"
import { Link } from "react-router-dom"
import type { DnsCheck, DnsMode } from "@/api/dns"
import { DnsNotes } from "@/components/DnsMark"
import { choiceKey, helpKey, lacksServers, linesText } from "@/components/dnsWords"
import type { DnsView } from "@/components/dnsWords"
import { Help, ListLine, Pick } from "@/components/fields"
import { label } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

// Where a choice stands: the server picks for everyone, a template and a client may take the level above.
export type DnsLevel = "server" | "template" | "client"

// The choices of a level, the level above first where there is one.
const modes: Record<DnsLevel, DnsMode[]> = {
  server: ["routing", "panel", "network", "own", "endpoint"],
  template: ["auto", "routing", "panel", "network", "own", "endpoint"],
  client: ["auto", "routing", "panel", "network", "own", "endpoint"],
}

// Where the name servers of the files come from, with the note of the item picked behind the question mark; above
// names the item that takes the level above.
export function DnsPick({
  id,
  level,
  mode,
  above,
  onChange,
  locked = false,
}: {
  id: string
  level: DnsLevel
  mode: DnsMode
  above?: string
  onChange: (mode: DnsMode) => void
  locked?: boolean
}) {
  const t = useText()
  const shown = locked ? "panel" : mode

  return (
    <Pick
      id={id}
      caption={
        <span className="flex items-center gap-1">
          {t(level === "server" ? "dnsPick.serverCaption" : "dnsPick.caption")}
          <Help text={t(helpKey(shown))} />
        </span>
      }
      value={shown}
      onChange={(value) => onChange(value as DnsMode)}
      disabled={locked}
    >
      {modes[level].map((one) => (
        <option key={one} value={one}>
          {one === "auto" ? (above ?? t("dnsPick.auto")) : t(choiceKey(one))}
        </option>
      ))}
    </Pick>
  )
}

// The name servers listed next to the choice.
export function DnsServers({
  id,
  mode,
  servers,
  onChange,
  caption = "dnsPick.servers",
}: {
  id: string
  mode: DnsMode
  servers: string[]
  onChange: (servers: string[]) => void
  caption?: TextKey
}) {
  const t = useText()

  return (
    <ListLine
      id={id}
      caption={t(caption)}
      value={servers}
      onChange={onChange}
      fault={lacksServers(mode, servers) ? t("dnsPick.serversNeeded") : ""}
    />
  )
}

// What the server picks for the files, as a way to the page where it is picked.
export function DnsServerChoice({ mode }: { mode: DnsMode | undefined }) {
  const t = useText()

  if (mode === undefined) {
    return null
  }

  return (
    <div className="flex items-center text-sm sm:col-span-2">
      <span className={`${label} mr-1`}>{t("dnsPick.serverCaption")}:</span>
      <Link to="/routing/dns" className="text-brand-ink hover:text-brand-lit">
        {t(choiceKey(mode))}
      </Link>
    </div>
  )
}

// What a file of the draft would name and what the draft would contradict, what is given between them standing there.
export function DnsOutcome({
  check,
  caption,
  view,
  skip,
  members,
  children,
}: {
  check: DnsCheck | undefined
  caption: TextKey
  view: DnsView
  skip?: string[]
  members?: number[]
  children?: ReactNode
}) {
  const t = useText()

  if (check === undefined) {
    return children ?? null
  }

  return (
    <>
      {check.lines.length > 0 && <div className="text-sm text-muted sm:col-span-2">{linesText(t, check.lines, caption)}</div>}
      {children}
      <DnsNotes findings={check.findings} view={view} skip={skip} members={members} />
    </>
  )
}
