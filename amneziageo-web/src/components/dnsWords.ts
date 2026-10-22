import type { DnsAffected, DnsFinding, DnsLevel, DnsLine, DnsMode, DnsSubject } from "@/api/dns"
import type { Text, TextKey } from "@/i18n"

// Whose eyes a finding is told for: the page of the resolver, a rule, a template, an interface or a client.
export type DnsView = "dns" | "rule" | "template" | "config" | "client"

// What tells a finding apart in a view.
export interface DnsContext {
  port: number
  draft: boolean
  everyone: (subject: DnsSubject) => boolean
}

export interface Said {
  key: string
  text: string
  level: DnsLevel
  rows: Row[]
}

export interface Row {
  key: string
  id: number
  name: string
  how: string
}

export type Words = Record<Intl.LDMLPluralRule, TextKey>

export const ruleWords: Words = {
  zero: "dnsCheck.ruleMany",
  one: "dnsCheck.ruleOne",
  two: "dnsCheck.ruleFew",
  few: "dnsCheck.ruleFew",
  many: "dnsCheck.ruleMany",
  other: "dnsCheck.ruleOther",
}

export const templateWords: Words = {
  zero: "dnsCheck.templateMany",
  one: "dnsCheck.templateOne",
  two: "dnsCheck.templateFew",
  few: "dnsCheck.templateFew",
  many: "dnsCheck.templateMany",
  other: "dnsCheck.templateOther",
}

export const clientWords: Words = {
  zero: "dnsCheck.clientMany",
  one: "dnsCheck.clientOne",
  two: "dnsCheck.clientFew",
  few: "dnsCheck.clientFew",
  many: "dnsCheck.clientMany",
  other: "dnsCheck.clientOther",
}

// The clients after "for" and "of": "2 clients", "1 клиента".
export const clientsOfWords: Words = {
  zero: "dnsCheck.clientsOfMany",
  one: "dnsCheck.clientsOfOne",
  two: "dnsCheck.clientsOfMany",
  few: "dnsCheck.clientsOfMany",
  many: "dnsCheck.clientsOfMany",
  other: "dnsCheck.clientsOfOther",
}

// Says a count with the word that fits it.
export function counted(t: Text, language: string, words: Words, count: number): string {
  return t(words[new Intl.PluralRules(language).select(count)], { count })
}

// The worst level among findings, hints left out.
export function levelOf(findings: DnsFinding[]): DnsLevel | null {
  if (findings.some((one) => one.level === "error")) {
    return "error"
  }

  return findings.some((one) => one.level === "warning") ? "warning" : null
}

// The names of a few, the rest counted.
export function fewNames(t: Text, names: string[], shown = 3): string {
  const head = names.slice(0, shown).join(", ")

  return names.length > shown ? `${head}, ${t("dnsCheck.more", { count: names.length - shown })}` : head
}

// The name of a choice of the name servers.
export function choiceKey(mode: DnsMode): TextKey {
  switch (mode) {
    case "routing":
      return "dnsPick.routing"
    case "panel":
      return "dnsPick.panel"
    case "network":
      return "dnsPick.network"
    case "own":
      return "dnsPick.own"
    case "endpoint":
      return "dnsPick.endpoint"
    default:
      return "dnsPick.auto"
  }
}

// The note of a choice: what a client of another make does with it and whether the panel sees its names.
export function helpKey(mode: DnsMode): TextKey {
  switch (mode) {
    case "routing":
      return "dnsPick.helpRouting"
    case "panel":
      return "dnsPick.helpPanel"
    case "network":
      return "dnsPick.helpNetwork"
    case "own":
      return "dnsPick.helpOwn"
    case "endpoint":
      return "dnsPick.helpEndpoint"
    default:
      return "dnsPick.helpAuto"
  }
}

// The line of a file in a form: what DNS it names and why the resolver is there.
export function lineText(t: Text, line: DnsLine, caption: TextKey): string {
  if (line.servers.length === 0) {
    return t(`${caption}None` as TextKey)
  }

  const told = t(caption, { servers: line.servers.join(", ") })
  const said = why(t, line)

  return said === "" ? told : `${told} ${said}`
}

// The lines of the files at several endpoints, less those that lose the resolver: one line, the endpoint at each address.
export function linesText(t: Text, lines: DnsLine[], caption: TextKey): string {
  const kept = lines.filter((one) => !one.lost)
  const shown = kept.length > 0 ? kept : lines
  const first = shown[0]
  if (shown.length === lines.length && shown.every((one) => one.servers.join(", ") === first.servers.join(", "))) {
    return lineText(t, first, caption)
  }

  const whys = shown.map((one) => why(t, one))
  const shared = whys.every((one) => one === whys[0])
  const parts = shown.map((one, at) => {
    const named =
      one.servers.length === 0
        ? t("dnsCheck.atNone", { endpoint: one.endpoint })
        : t("dnsCheck.at", { servers: one.servers.join(", "), endpoint: one.endpoint })

    return shared || whys[at] === "" ? named : `${named} ${whys[at]}`
  })
  const told = t(caption, { servers: parts.join(", ") })

  return shared && whys[0] !== "" ? `${told} ${whys[0]}` : told
}

// Why the resolver of the panel is in a file, empty when it is not.
function why(t: Text, line: DnsLine): string {
  if (!line.isPanel) {
    return ""
  }

  if (line.from === "standins") {
    return t("dnsCheck.whyStandIns")
  }

  return t(line.choice === "routing" || line.choice === "auto" ? "dnsCheck.whyRouting" : "dnsCheck.whyPanel")
}

// The colour of a level.
export function tone(level: DnsLevel): string {
  if (level === "error") {
    return "text-alarm"
  }

  return level === "warning" ? "text-warn" : "text-muted"
}

// Tells a finding in a view: one sentence and, where the clients matter, a row for each.
export function tell(t: Text, language: string, finding: DnsFinding, view: DnsView, context: DnsContext): Said {
  const key = `${finding.code}:${finding.subjects.map((one) => `${one.kind}${one.id}`).join(",")}`
  const named = (kind: string) => finding.subjects.find((one) => one.kind === kind)
  const rule = named("rule") ?? named("basic")
  const template = named("template")
  const config = named("config")
  const servers = [...new Set(finding.clients.flatMap((one) => one.servers))].join(", ")
  const clients = counted(t, language, clientsOfWords, finding.clients.length)
  const ruleName = rule === undefined ? "" : nameOf(t, rule)
  const said = (text: string, rows: Row[] = []): Said => ({ key, text, level: finding.level, rows })
  const how = (one: DnsAffected): Row => ({ key: String(one.id), id: one.id, name: one.name, how: source(t, one) })
  const only = (one: DnsAffected, text = ""): Row => ({ key: String(one.id), id: one.id, name: one.name, how: text })

  switch (finding.code) {
    case "rule-off":
      if (view === "dns") {
        const whom = rule !== undefined && (rule.kind === "basic" || context.everyone(rule))
        return said(
          rule?.kind === "basic"
            ? t("dnsCheck.basicEveryone", { name: ruleName })
            : t(whom ? "dnsCheck.ruleEveryone" : "dnsCheck.ruleSome", { name: ruleName, clients }),
        )
      }

      if (view === "rule") {
        return said(t(context.draft ? "dnsCheck.ruleOffDraft" : "dnsCheck.ruleOffHere"))
      }

      return said(t(rule?.kind === "basic" ? "dnsCheck.basicOff" : "dnsCheck.ruleOff", { name: ruleName }))
    case "standins-off":
      if (view === "dns") {
        return said(t("dnsCheck.standInsSome", { name: template?.name ?? "", clients }))
      }

      return said(t(view === "template" ? "dnsCheck.standInsOffHere" : "dnsCheck.standInsOff", { name: template?.name ?? "" }))
    case "holders-off":
      if (view === "dns") {
        return said("", finding.clients.map((one) => only(one, t("dnsCheck.holderLine"))))
      }

      return view === "client"
        ? said(t("dnsCheck.holdersOffClient"))
        : said(t("dnsCheck.holdersOff"), finding.clients.map((one) => only(one)))
    case "panel-off":
      if (view === "dns") {
        return said("", finding.clients.map((one) => only(one, t("dnsCheck.pickedLine", { servers: one.servers.join(", ") }))))
      }

      return view === "client"
        ? said(t("dnsCheck.panelOffClient", { servers }))
        : said(t("dnsCheck.panelOff", { servers }), finding.clients.map(how))
    case "port":
      return said(t("dnsCheck.port", { port: context.port, servers }), view === "client" ? [] : finding.clients.map(how))
    case "listen":
      return said(t("dnsCheck.listen", { config: config?.name ?? "", servers }), view === "client" ? [] : finding.clients.map(how))
    case "kept":
      return said(t("dnsCheck.kept", { config: config?.name ?? "", servers }), view === "client" ? [] : finding.clients.map(how))
    case "rule-outside":
      if (view === "client") {
        return said(t("dnsCheck.ruleOutsideClient", { name: ruleName, servers }))
      }

      if (view === "rule") {
        return said(t("dnsCheck.ruleOutsideHere", { clients }), finding.clients.map(how))
      }

      if (view === "template") {
        return said(t("dnsCheck.ruleOutsideTemplate", { name: ruleName, servers, clients }), finding.clients.map(how))
      }

      return said(t("dnsCheck.ruleOutside", { name: ruleName, clients }), finding.clients.map(how))
    case "outside":
      return said(servers === "" ? t("dnsCheck.outsideNetwork") : t("dnsCheck.outside", { servers }))
    case "unseen":
      return said(t("dnsCheck.unseen", { clients }), view === "client" ? [] : finding.clients.map(how))
    default:
      return said(finding.code)
  }
}

function nameOf(t: Text, subject: DnsSubject): string {
  if (subject.kind !== "basic") {
    return subject.name
  }

  return t(subject.id === -1 ? "basic.block" : "basic.direct")
}

// Where the name servers of a client come from, said short.
function source(t: Text, one: DnsAffected): string {
  const servers = one.servers.join(", ")
  if (one.servers.length === 0) {
    return t(one.from === "client" ? "dnsCheck.fromClientNetwork" : one.from === "server" ? "dnsCheck.fromServerNetwork" : "dnsCheck.fromTemplateNetwork", {
      source: one.source,
    })
  }

  if (one.from === "client") {
    return t("dnsCheck.fromClient", { servers })
  }

  if (one.from === "server") {
    return t(one.choice === "own" ? "dnsCheck.fromServer" : "dnsCheck.fromConfig", { source: one.source, servers })
  }

  return t("dnsCheck.fromTemplate", { source: one.source, servers })
}

// Tells whether the choice lacks the servers it needs.
export function lacksServers(mode: DnsMode, servers: string[], locked = false): boolean {
  return mode === "own" && !locked && servers.length === 0
}
