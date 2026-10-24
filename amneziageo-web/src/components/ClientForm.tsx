import { useState } from "react"
import { Link } from "react-router-dom"
import { nth, numberOf, parse, reserved, span, write } from "@/address"
import type { Span } from "@/address"
import { complaint } from "@/api/auth"
import { useClients } from "@/api/clients"
import type { Client, ClientDraft, Inbound } from "@/api/clients"
import { useConfigs } from "@/api/configs"
import type { Config } from "@/api/configs"
import { useDnsCheck, useResolver } from "@/api/dns"
import { useRules } from "@/api/rules"
import type { Rule } from "@/api/rules"
import { scopes } from "@/api/scopes"
import { useTemplates } from "@/api/templates"
import { AppCaption } from "@/components/AppMark"
import { DnsOutcome, DnsPick, DnsServers } from "@/components/DnsField"
import { choiceKey, lacksServers } from "@/components/dnsWords"
import { Help, Line, LinkLine, Links, Part, Pick, Regenerate, Switch } from "@/components/fields"
import { few, names, ruleItems } from "@/components/members"
import { danger, field, footer, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { randomId, randomKey } from "@/keys"
import { holds } from "@/store/authSlice"
import { same } from "@/store/draftSlice"
import { useAppSelector } from "@/store/hooks"

export function ClientForm({
  start,
  self,
  pending,
  error,
  onSave,
  onClose,
  onRemove,
}: {
  start: ClientDraft
  self?: number
  pending: boolean
  error: unknown
  onSave: (draft: ClientDraft) => void
  onClose: () => void
  onRemove?: () => void
}) {
  const t = useText()
  const configs = useConfigs().data ?? []
  const others = (useClients().data ?? []).filter((one) => one.id !== self)
  const templates = useTemplates()
  const [draft, setDraft] = useState(start)
  const [number, setNumber] = useState<string | null>(null)
  const [text, setText] = useState<string | null>(null)
  const [limit, setLimit] = useState(gigabytes(start.dailyLimit))
  const spans = spansOf(configs, draft.configId)
  const taken = holders(others, draft.configId)
  const legacy = start.address.length > 0 && carried(spans, start.address) === null
  const base = start.address.length > 0 ? (carried(spans, start.address) ?? "") : first(spans, taken)
  const shown = number ?? base
  const listed = text ?? start.address.join(", ")
  const whole = /^\d+$/.test(shown)
  const problem = legacy ? "" : fault(t, spans, taken, shown)
  const head = spans[0]
  const prefix =
    head !== undefined && !head.six && head.bits >= 24 && head.network % 256n === 0n
      ? write(false, head.network).slice(0, -1)
      : ""
  const rest = prefix.length > 0 ? spans.slice(1) : spans
  const derived = whole ? rest.map((one) => nth(one, BigInt(shown))).join(", ") : ""
  const clash = others.some((one) => one.name.toLowerCase() === draft.name.trim().toLowerCase())
  const misnamed = !/^[A-Za-z0-9_-]{0,64}$/.test(draft.subscriptionId)
  const allowed = allowanceOf(limit)
  const check = useDnsCheck(draft.configId > 0 ? { id: self, client: draft } : null)
  const files = useResolver(false).data?.filesMode ?? "routing"
  const template = (templates.data ?? []).find((one) => one.id === draft.templateId)
  const above =
    template === undefined
      ? t("dnsPick.asServer", { choice: t(choiceKey(files)) })
      : t("dnsPick.asTemplate", {
          choice: t(choiceKey(template.standIns ? "panel" : template.dnsMode === "auto" ? files : template.dnsMode)),
        })
  const user = useAppSelector((s) => s.auth.user)
  const rules = useRules().data ?? []
  const mayRoute = holds(user, scopes.manageRouting)
  const endpoint = configs.find((one) => one.id === draft.configId)?.name ?? ""
  const naming = start.name.length === 0 ? [] : rules.filter((one) => names(one, "clients", start.name))
  const picked = draft.rules ?? naming.map((one) => one.id)
  const lastOnes = naming.filter((one) => one.clients.length === 1).map((one) => String(one.id))
  const elsewhere = (one: Rule) => endpoint.length > 0 && one.inbounds.length > 0 && !one.inbounds.includes(endpoint)
  const ruled = ruleItems(
    rules.filter((one) => one.clients.length > 0),
    (one) => few(one.clients, (count) => t("templates.more", { count: String(count) })),
    (one) => (elsewhere(one) ? t("clients.ruleElsewhere", { name: endpoint }) : undefined),
  )
  const everyone = rules.filter((one) => one.clients.length === 0 && !elsewhere(one))
  const edited =
    !same(draft, start) ||
    shown !== base ||
    listed !== start.address.join(", ") ||
    limit !== gigabytes(start.dailyLimit)
  const ready =
    edited &&
    !pending &&
    draft.name.trim().length > 0 &&
    !clash &&
    !misnamed &&
    allowed !== null &&
    draft.configId > 0 &&
    !lacksServers(draft.dnsMode, draft.dns) &&
    (legacy ? parts(listed).length > 0 : whole && spans.length > 0 && problem.length === 0)

  function put(change: Partial<ClientDraft>) {
    setDraft({ ...draft, ...change })
  }

  function pick(configId: number) {
    setDraft({ ...draft, configId })
    setNumber(first(spansOf(configs, configId), holders(others, configId)))
  }

  function addresses(): string[] {
    if (legacy) {
      return parts(listed)
    }

    return spans.map((one) => `${nth(one, BigInt(shown))}/${one.six ? 128 : 32}`)
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("clients.partMain")}>
        <div className="sm:col-span-2">
          <Switch
            id="client-enabled"
            caption={t("clients.enabled")}
            value={draft.isEnabled}
            onChange={(isEnabled) => put({ isEnabled })}
          />
        </div>
        <div className="sm:col-span-2">
          <Pick
            id="client-interface"
            caption={t("clients.endpointName")}
            value={draft.configId === 0 ? "" : String(draft.configId)}
            onChange={(value) => pick(Number(value))}
            disabled={self !== undefined}
          >
            <option value="">{t("clients.noConfig")}</option>
            {configs.map((one) => (
              <option key={one.id} value={one.id}>
                {one.name}
              </option>
            ))}
          </Pick>

          {draft.configId > 0 && (
            <div className="mt-1 flex justify-end">
              <Link to={`/interfaces/${draft.configId}/edit`} className="text-sm text-brand-ink hover:text-brand-lit">
                {t("action.goTo")}
              </Link>
            </div>
          )}
        </div>

        <Line
          id="client-name"
          caption={t("clients.name")}
          value={draft.name}
          onChange={(name) => put({ name })}
          fault={clash ? t("error.clientNameTaken") : ""}
        />

        <Line
          id="client-preshared"
          caption={t("clients.preshared")}
          value={draft.presharedKey}
          onChange={(presharedKey) => put({ presharedKey })}
          after={<Regenerate title={t("clients.generate")} onClick={() => put({ presharedKey: randomKey() })} />}
        />

        {legacy ? (
          <Line id="client-address" caption={t("clients.address")} value={listed} onChange={setText} wide />
        ) : (
          <div className="sm:col-span-2">
            <label className={label} htmlFor="client-address">
              {t("clients.address")}
            </label>
            <div className="mt-1 flex items-center gap-2">
              {prefix.length > 0 && <span className="text-sm text-ink">{prefix}</span>}
              <div className="w-24 shrink-0">
                <input
                  id="client-address"
                  inputMode="numeric"
                  value={shown}
                  disabled={draft.configId === 0}
                  onChange={(e) => setNumber(e.target.value.trim())}
                  className={field}
                />
              </div>
              {derived.length > 0 && <span className="truncate text-sm text-muted">{derived}</span>}
            </div>
            {problem.length > 0 && <div className="mt-1 text-xs text-alarm">{problem}</div>}
          </div>
        )}

        <Line id="client-note" caption={t("clients.note")} value={draft.note} onChange={(note) => put({ note })} />
      </Part>

      <Part title={t("clients.template")}>
        <div>
          <Pick
            id="client-template"
            caption={t("clients.template")}
            value={draft.templateId === null ? "" : String(draft.templateId)}
            onChange={(value) => put({ templateId: value === "" ? null : Number(value) })}
          >
            <option value="">{t("clients.noTemplate")}</option>
            {(templates.data ?? []).map((one) => (
              <option key={one.id} value={one.id}>
                {one.name}
              </option>
            ))}
          </Pick>

          <div className="mt-1 flex justify-end">
            <Link
              to={
                draft.templateId === null
                  ? "/clients/templates/default"
                  : `/clients/templates/${draft.templateId}/edit`
              }
              className="text-sm text-brand-ink hover:text-brand-lit"
            >
              {t("action.goTo")}
            </Link>
          </div>
        </div>

        <DnsPick id="client-dns-mode" level="client" mode={draft.dnsMode} above={above} onChange={(dnsMode) => put({ dnsMode })} />

        {draft.dnsMode === "own" && (
          <>
            <div className="hidden sm:block" />
            <DnsServers id="client-dns" mode={draft.dnsMode} servers={draft.dns} onChange={(dns) => put({ dns })} />
          </>
        )}

        <DnsOutcome check={check.data} caption="dnsCheck.lineClient" view="client" />
      </Part>

      <Part title={t("clients.partRouting")}>
        <div className="sm:col-span-2">
          <span className={label}>{t("clients.rules")}</span>
          <div className="mt-1">
            <Links
              id="client-rules"
              value={picked.map(String)}
              items={ruled}
              placeholder={t(mayRoute ? "clients.addRule" : "clients.dash")}
              fixed={lastOnes}
              onChange={mayRoute ? (keys) => put({ rules: keys.map(Number) }) : undefined}
            />
          </div>
        </div>
        {everyone.length > 0 && (
          <div className="sm:col-span-2">
            <span className={label}>{t("clients.rulesForAll")}</span>
            <div className="mt-1 text-sm text-ink">
              <LinkLine items={ruleItems(everyone)} />
            </div>
          </div>
        )}
      </Part>

      <Part title={t("clients.partAccess")}>
        <Line
          id="client-limit"
          caption={t("clients.limit")}
          value={limit}
          placeholder={t("clients.noLimit")}
          onChange={(value) => setLimit(value.trim())}
          fault={allowed === null ? t("error.badClientLimit") : ""}
        />

        <Line
          id="client-subscription"
          caption={<AppCaption caption={t("clients.subscription")} what="subscription" />}
          value={draft.subscriptionId}
          onChange={(subscriptionId) => put({ subscriptionId: subscriptionId.trim() })}
          fault={misnamed ? t("error.badClientSubscription") : ""}
          after={<Regenerate title={t("clients.generate")} onClick={() => put({ subscriptionId: randomId() })} />}
        />

        <Pick
          id="client-inbound"
          caption={
            <span className="flex items-center gap-1">
              {t("clients.inbound")}
              <Help text={t("clients.inboundHint")} />
            </span>
          }
          value={draft.inbound}
          onChange={(value) => put({ inbound: value as Inbound })}
        >
          <option value="endpoint">{t("clients.inboundEndpoint")}</option>
          <option value="off">{t("clients.inboundOff")}</option>
          <option value="server">{t("clients.inboundServer")}</option>
          <option value="network">{t("clients.inboundNetwork")}</option>
        </Pick>

        <div className="flex items-end sm:pb-2">
          <Switch
            id="client-disconnect"
            caption={<AppCaption caption={t("clients.disconnect")} what="signal" />}
            value={draft.disconnect}
            onChange={(disconnect) => put({ disconnect })}
          />
        </div>
      </Part>

      {error !== null && error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(error) as TextKey)}</div>
      )}

      <div className={footer}>
        {onRemove !== undefined && (
          <button type="button" onClick={onRemove} className={`mr-auto ${danger}`}>
            {t("clients.remove")}
          </button>
        )}
        <button type="button" onClick={onClose} disabled={!edited || pending} className={secondary}>
          {t("clients.cancel")}
        </button>
        <button
          type="button"
          onClick={() => onSave({ ...draft, name: draft.name.trim(), address: addresses(), dailyLimit: allowed ?? 0 })}
          disabled={!ready}
          className={primary}
        >
          {pending ? t("clients.busy") : t("clients.save")}
        </button>
      </div>
    </div>
  )
}

function spansOf(configs: Config[], configId: number): Span[] {
  const endpoint = configs.find((one) => one.id === configId)

  return (endpoint?.address ?? []).map((one) => span(one)).filter((one) => one !== null)
}

function holders(clients: Client[], configId: number): Map<string, string> {
  const found = new Map<string, string>()
  for (const one of clients) {
    if (one.configId !== configId) {
      continue
    }

    for (const address of one.address) {
      const point = parse(address.split("/")[0] ?? "")
      if (point !== null) {
        found.set(key(point.six, point.value), one.name)
      }
    }
  }

  return found
}

function key(six: boolean, value: bigint): string {
  return `${six ? 6 : 4}:${value}`
}

function carried(spans: Span[], addresses: string[]): string | null {
  if (spans.length === 0 || addresses.length !== spans.length) {
    return null
  }

  const numbers = spans.map((one) => {
    const inside = addresses.map((address) => numberOf(one, address)).filter((found) => found !== null)

    return inside.length === 1 ? inside[0] : null
  })
  const head = numbers[0]

  return head !== null && head !== undefined && numbers.every((one) => one === head) ? String(head) : null
}

function first(spans: Span[], taken: Map<string, string>): string {
  if (spans.length === 0) {
    return ""
  }

  const room = spans.reduce((least, one) => (one.size < least ? one.size : least), 65536n)
  for (let number = 1n; number < room; number++) {
    if (spans.every((one) => !reserved(one, number) && !taken.has(key(one.six, one.network + number)))) {
      return String(number)
    }
  }

  return ""
}

function fault(t: Text, spans: Span[], taken: Map<string, string>, shown: string): string {
  if (!/^\d+$/.test(shown)) {
    return ""
  }

  const number = BigInt(shown)
  for (const one of spans) {
    if (number >= one.size) {
      return t("error.clientAddressOutside")
    }

    if (reserved(one, number)) {
      return t("error.clientAddressReserved")
    }

    const holder = taken.get(key(one.six, one.network + number))
    if (holder !== undefined) {
      return t("clients.addressTakenBy", { name: holder })
    }
  }

  return ""
}

const gibibyte = 1024 ** 3

function gigabytes(value: number): string {
  return value > 0 ? String(Number((value / gibibyte).toFixed(2))) : ""
}

function allowanceOf(text: string): number | null {
  if (text.length === 0) {
    return 0
  }

  if (!/^\d+([.,]\d+)?$/.test(text)) {
    return null
  }

  const value = Math.round(Number(text.replace(",", ".")) * gibibyte)

  return value > 0 && value <= 2 ** 50 ? value : null
}

function parts(text: string): string[] {
  return text
    .split(/[\s,]+/)
    .map((one) => one.trim())
    .filter((one) => one.length > 0)
}
