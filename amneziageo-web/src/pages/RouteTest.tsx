import { useState } from "react"
import type { FormEvent } from "react"
import { Link } from "react-router-dom"
import { complaint, reason } from "@/api/auth"
import { useClients } from "@/api/clients"
import { blockList, directList, useRouteTest, useRules } from "@/api/rules"
import type { RouteAnswer, RouteMember, RouteStep } from "@/api/rules"
import { scopes } from "@/api/scopes"
import { Line, Pick } from "@/components/fields"
import { card, primary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

interface Question {
  target: string
  port: string
  protocol: "tcp" | "udp"
  client: string
  sourcePort: string
}

const start: Question = { target: "", port: "443", protocol: "tcp", client: "", sourcePort: "" }

export function RouteTest() {
  const t = useText()
  const clients = useClients()
  const rules = useRules()
  const test = useRouteTest()
  const user = useAppSelector((s) => s.auth.user)
  const may = holds(user, scopes.manageRouting)
  const [question, setQuestion] = useState(start)
  const places = new Map((rules.data ?? []).map((one, at) => [one.id, at + 1]))

  function put(change: Partial<Question>) {
    setQuestion({ ...question, ...change })
  }

  function ask(event: FormEvent) {
    event.preventDefault()
    test.mutate({
      target: question.target.trim(),
      port: number(question.port),
      protocol: question.protocol,
      client: question.client,
      sourcePort: number(question.sourcePort),
    })
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <form onSubmit={ask} className={`grid grid-cols-1 gap-3.5 p-4.5 sm:grid-cols-2 lg:grid-cols-6 ${card}`}>
        <Line
          id="test-target"
          caption={t("test.target")}
          value={question.target}
          onChange={(target) => put({ target })}
          wide
        />
        <Line id="test-port" caption={t("test.port")} value={question.port} onChange={(port) => put({ port })} />
        <Pick
          id="test-protocol"
          caption={t("rules.protocol")}
          value={question.protocol}
          onChange={(protocol) => put({ protocol: protocol === "udp" ? "udp" : "tcp" })}
        >
          <option value="tcp">TCP</option>
          <option value="udp">UDP</option>
        </Pick>
        <Pick id="test-client" caption={t("test.client")} value={question.client} onChange={(client) => put({ client })}>
          <option value="">{t("rules.pick")}</option>
          {(clients.data ?? []).map((one) => (
            <option key={one.id} value={one.name}>
              {one.name}
            </option>
          ))}
        </Pick>
        <Line
          id="test-source-port"
          caption={t("test.sourcePort")}
          value={question.sourcePort}
          onChange={(sourcePort) => put({ sourcePort })}
        />
        <div className="flex justify-end sm:col-span-2 lg:col-span-6">
          <button type="submit" disabled={test.isPending || question.target.trim().length === 0} className={primary}>
            {test.isPending ? t("test.busy") : t("test.run")}
          </button>
        </div>
      </form>

      {test.error !== null && <div className="text-sm text-alarm">{t(complaint(test.error))}</div>}

      {test.data && <Answer answer={test.data} places={places} may={may} t={t} />}
    </div>
  )
}

function Answer({
  answer,
  places,
  may,
  t,
}: {
  answer: RouteAnswer
  places: Map<number, number>
  may: boolean
  t: Text
}) {
  return (
    <>
      <div className={`flex flex-col gap-4 p-4.5 ${card}`}>
        <div className={`text-lg font-semibold ${tone(answer)}`}>{verdict(answer, t)}</div>

        <dl className="grid grid-cols-[minmax(0,9rem)_minmax(0,1fr)] gap-x-4 gap-y-2 text-sm">
          <dt className="text-muted">{t("test.rule")}</dt>
          <dd className="min-w-0 text-body">
            <RuleName id={answer.rule?.id ?? null} name={answer.rule?.name ?? ""} places={places} may={may} t={t} />
          </dd>

          {answer.exit && (
            <>
              <dt className="text-muted">{t("rules.outbound")}</dt>
              <dd className="min-w-0 text-body">
                {answer.exit.name}
                {answer.exit.isGroup && (
                  <span className="ml-1.5 rounded bg-chip px-1.5 py-0.5 text-xs text-chip-ink">
                    {t(strategy(answer.exit.strategy))}
                  </span>
                )}
                {answer.exit.members.length > 0 && (
                  <ul className="mt-1.5 flex flex-col gap-1">
                    {answer.exit.members.map((member) => (
                      <li key={member.name} className="flex gap-2">
                        <span>{member.name}</span>
                        <span className={member.carries ? "text-good" : "text-muted"}>{t(standing(member))}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </dd>
            </>
          )}

          <dt className="text-muted">{t("test.addresses")}</dt>
          <dd className="min-w-0 break-all text-body">
            {answer.addresses.length > 0 ? answer.addresses.join(", ") : t("test.unresolved")}
          </dd>

          {answer.inbound.length > 0 && (
            <>
              <dt className="text-muted">{t("rules.inbounds")}</dt>
              <dd className="min-w-0 text-body">{answer.inbound}</dd>
            </>
          )}

          {answer.sources.length > 0 && (
            <>
              <dt className="text-muted">{t("rules.sources")}</dt>
              <dd className="min-w-0 break-all text-body">{answer.sources.join(", ")}</dd>
            </>
          )}
        </dl>
      </div>

      {answer.steps.length > 0 && (
        <div className={`overflow-x-auto ${card}`}>
          <table className="w-full text-left text-sm">
            <thead className="text-xs text-faint">
              <tr>
                <th className="px-4 py-2.5 font-normal">#</th>
                <th className="px-4 py-2.5 font-normal">{t("rules.name")}</th>
                <th className="px-4 py-2.5 font-normal">{t("test.outcome")}</th>
                <th className="px-4 py-2.5 font-normal">{t("test.why")}</th>
              </tr>
            </thead>
            <tbody>
              {answer.steps.map((step) => (
                <tr key={step.rule} className="border-t border-line-soft">
                  <td className="px-4 py-3 text-faint tabular-nums">{places.get(step.rule) ?? ""}</td>
                  <td className="px-4 py-3 text-ink">{listName(step.rule, step.name, t)}</td>
                  <td className={`px-4 py-3 ${step.outcome === "match" ? "text-good" : "text-muted"}`}>
                    {t(outcome(step))}
                  </td>
                  <td className="px-4 py-3 break-all text-body">{why(step, t)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}

function RuleName({
  id,
  name,
  places,
  may,
  t,
}: {
  id: number | null
  name: string
  places: Map<number, number>
  may: boolean
  t: Text
}) {
  if (id === null) {
    return <span className="text-muted">{t("test.noRule")}</span>
  }

  if (id === blockList || id === directList) {
    return (
      <Link to="/routing/basic" className="hover:text-brand-ink">
        {listName(id, name, t)}
      </Link>
    )
  }

  const text = `#${places.get(id) ?? ""} ${name}`

  return may ? (
    <Link to={`/routing/rules/${id}/edit`} className="hover:text-brand-ink">
      {text}
    </Link>
  ) : (
    <span>{text}</span>
  )
}

function listName(id: number, name: string, t: Text): string {
  if (id === blockList) {
    return t("basic.block")
  }

  return id === directList ? t("basic.direct") : name
}

function number(text: string): number | null {
  const value = Number(text.trim())

  return text.trim() === "" || !Number.isInteger(value) ? null : value
}

function tone(answer: RouteAnswer): string {
  if (answer.verdict === "out") {
    return "text-good"
  }

  if (answer.verdict === "host") {
    return "text-ink"
  }

  return answer.verdict === "held" ? "text-warn" : "text-alarm"
}

function verdict(answer: RouteAnswer, t: Text): string {
  switch (answer.verdict) {
    case "out":
      return t("test.out", { name: answer.exit?.name ?? answer.rule?.outbound ?? "" })
    case "host":
      return t("test.host")
    case "block":
      return t("test.block")
    case "held":
      return t("test.held", { name: answer.rule?.outbound ?? "" })
    default:
      return t("test.guard", { guard: answer.guard === "dot" ? "DoT" : "DoH" })
  }
}

function strategy(name: string): TextKey {
  if (name === "round") {
    return "balancers.round"
  }

  return name === "sticky" ? "balancers.sticky" : "balancers.priority"
}

function standing(member: RouteMember): TextKey {
  if (!member.isEnabled) {
    return "test.memberOff"
  }

  if (!member.isAlive) {
    return "test.memberDown"
  }

  return member.carries ? "test.memberCarries" : "test.memberSpare"
}

function outcome(step: RouteStep): TextKey {
  if (step.outcome === "match") {
    return "test.match"
  }

  return step.outcome === "skip" ? "test.skip" : "test.miss"
}

function why(step: RouteStep, t: Text): string {
  switch (step.reason) {
    case "range":
      return t("test.byRange", { detail: step.detail })
    case "name":
      return t("test.byName", { detail: step.detail })
    case "resolved":
      return t("test.byResolved", { detail: step.detail })
    case "any":
      return t("test.byAny")
    case "target":
      return t("test.missTarget")
    case "inbound":
      return t("test.missInbound")
    case "source":
      return t("test.missSource")
    case "protocol":
      return t("test.missProtocol")
    case "port":
      return t("test.missPort")
    case "source-port":
      return t("test.missSourcePort")
    case "off":
      return t("rules.off")
    default:
      return t(reason(step.reason))
  }
}
