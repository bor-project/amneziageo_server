import type { DnsCheck, DnsMode } from "@/api/dns"
import { DnsNotes } from "@/components/DnsMark"
import { lacksServers, lineText } from "@/components/dnsWords"
import type { DnsView } from "@/components/dnsWords"
import { ListLine, Pick } from "@/components/fields"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

// Where the name servers of the files come from.
export function DnsPick({
  id,
  mode,
  onChange,
  locked = false,
}: {
  id: string
  mode: DnsMode
  onChange: (mode: DnsMode) => void
  locked?: boolean
}) {
  const t = useText()

  return (
    <Pick
      id={id}
      caption={t("dnsPick.caption")}
      value={locked ? "panel" : mode}
      onChange={(value) => onChange(value as DnsMode)}
      disabled={locked}
    >
      <option value="auto">{t("dnsPick.auto")}</option>
      <option value="panel">{t("dnsPick.panel")}</option>
      <option value="own">{t("dnsPick.own")}</option>
    </Pick>
  )
}

// The name servers listed next to the choice.
export function DnsServers({
  id,
  mode,
  servers,
  onChange,
}: {
  id: string
  mode: DnsMode
  servers: string[]
  onChange: (servers: string[]) => void
}) {
  const t = useText()

  return (
    <ListLine
      id={id}
      caption={t("dnsPick.servers")}
      value={servers}
      onChange={onChange}
      fault={lacksServers(mode, servers) ? t("dnsPick.serversNeeded") : ""}
    />
  )
}

// What a file of the draft would name and what the draft would contradict.
export function DnsOutcome({
  check,
  caption,
  view,
  skip,
}: {
  check: DnsCheck | undefined
  caption: TextKey
  view: DnsView
  skip?: string[]
}) {
  const t = useText()

  if (check === undefined) {
    return null
  }

  return (
    <>
      {check.line !== null && <div className="text-sm text-muted sm:col-span-2">{lineText(t, check.line, caption)}</div>}
      <DnsNotes findings={check.findings} view={view} skip={skip} />
    </>
  )
}
