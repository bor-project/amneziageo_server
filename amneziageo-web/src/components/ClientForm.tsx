import { useState } from "react"
import { nth, numberOf, parse, reserved, span, write } from "@/address"
import type { Span } from "@/address"
import { complaint } from "@/api/auth"
import { useClients } from "@/api/clients"
import type { Client, ClientDraft, Forward, Inbound } from "@/api/clients"
import { useConfigs } from "@/api/configs"
import type { Config } from "@/api/configs"
import { useTemplates } from "@/api/templates"
import { Flag, Folded, Help, Line, Multi, Part, Pick, Regenerate } from "@/components/fields"
import { card, danger, field, fieldBox, label, note, primary, quiet, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { randomId, randomKey } from "@/keys"

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
  const chosen = (templates.data ?? []).find((one) => one.id === draft.templateId)
  const spans = spansOf(configs, draft.configId)
  const taken = holders(others, draft.configId)
  const legacy = start.address.length > 0 && carried(spans, start.address) === null
  const shown = number ?? (start.address.length > 0 ? (carried(spans, start.address) ?? "") : first(spans, taken))
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
  const ported = draft.forwards.every((one) => isPort(one.from) && isPort(one.to))
  const ready =
    !pending &&
    draft.name.trim().length > 0 &&
    !clash &&
    !misnamed &&
    allowed !== null &&
    ported &&
    draft.configId > 0 &&
    (legacy ? parts(listed).length > 0 : whole && spans.length > 0 && problem.length === 0)

  function put(change: Partial<ClientDraft>) {
    setDraft({ ...draft, ...change })
  }

  function carry(at: number, change: Partial<Forward>) {
    put({ forwards: draft.forwards.map((one, index) => (index === at ? { ...one, ...change } : one)) })
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
        <Pick
          id="client-interface"
          caption={t("clients.endpointName")}
          value={draft.configId === 0 ? "" : String(draft.configId)}
          onChange={(value) => pick(Number(value))}
          disabled={self !== undefined}
          wide
        >
          <option value="">{t("clients.noConfig")}</option>
          {configs.map((one) => (
            <option key={one.id} value={one.id}>
              {one.name}
            </option>
          ))}
        </Pick>

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
        <Pick
          id="client-template"
          caption={t("clients.template")}
          value={draft.templateId === null ? "" : String(draft.templateId)}
          onChange={(value) => put({ templateId: value === "" ? null : Number(value) })}
          wide
        >
          <option value="">{t("clients.noTemplate")}</option>
          {(templates.data ?? []).map((one) => (
            <option key={one.id} value={one.id}>
              {one.name}
            </option>
          ))}
        </Pick>

        {chosen !== undefined && (
          <Folded caption={t("templates.values")}>
            <Fixed caption={t("templates.allowed")} value={chosen.entries.join(", ")} dash={t("clients.dash")} />
            <Fixed caption={t("templates.dns")} value={chosen.dns.join(", ")} dash={t("clients.dash")} />
            <Fixed
              caption={t("templates.mtu")}
              value={chosen.mtu === null ? "" : String(chosen.mtu)}
              dash={t("clients.dash")}
            />
            <Fixed
              caption={t("templates.keepalive")}
              value={chosen.keepalive === null ? "" : String(chosen.keepalive)}
              dash={t("clients.dash")}
            />
          </Folded>
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
          caption={t("clients.subscription")}
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

        <div className="flex flex-wrap items-center gap-6 sm:col-span-2">
          <Flag
            id="client-enabled"
            caption={t("clients.enabled")}
            value={draft.isEnabled}
            onChange={(isEnabled) => put({ isEnabled })}
          />
          <Flag
            id="client-devices"
            caption={t("clients.multiDevice")}
            value={draft.multiDevice}
            onChange={(multiDevice) => put({ multiDevice })}
          />
        </div>
      </Part>

      <Part title={t("clients.partNetwork")}>
        <div className="sm:col-span-2">
          <label className={label} htmlFor="client-routes">
            {t("clients.routes")}
          </label>
          <div className="mt-1">
            <Multi
              id="client-routes"
              value={draft.routes}
              offers={[]}
              placeholder={t("clients.noRoutes")}
              onChange={(routes) => put({ routes })}
            />
          </div>
          <div className={note}>{t("clients.routesNote")}</div>
        </div>

        <div className="sm:col-span-2">
          <div className={label}>{t("clients.forwards")}</div>
          <div className="mt-1 flex flex-col gap-2">
            {draft.forwards.map((one, at) => (
              <div key={at} className="flex items-center gap-2">
                <select
                  value={one.protocol}
                  onChange={(e) => carry(at, { protocol: e.target.value as Forward["protocol"] })}
                  className={`w-24 shrink-0 ${fieldBox}`}
                >
                  <option value="tcp">tcp</option>
                  <option value="udp">udp</option>
                </select>
                <input
                  inputMode="numeric"
                  value={one.from === 0 ? "" : String(one.from)}
                  placeholder={t("clients.forwardFrom")}
                  onChange={(e) => carry(at, { from: portOf(e.target.value) })}
                  className={`w-28 shrink-0 ${fieldBox}`}
                />
                <span className="text-sm text-muted">{t("clients.forwardTo")}</span>
                <input
                  inputMode="numeric"
                  value={one.to === 0 ? "" : String(one.to)}
                  placeholder={t("clients.forwardPort")}
                  onChange={(e) => carry(at, { to: portOf(e.target.value) })}
                  className={`w-28 shrink-0 ${fieldBox}`}
                />
                <button
                  type="button"
                  className={quiet}
                  title={t("clients.forwardRemove")}
                  onClick={() => put({ forwards: draft.forwards.filter((_, index) => index !== at) })}
                >
                  &times;
                </button>
              </div>
            ))}
            <div>
              <button
                type="button"
                className={secondary}
                onClick={() => put({ forwards: [...draft.forwards, { protocol: "tcp", from: 0, to: 0 }] })}
              >
                {t("clients.forwardAdd")}
              </button>
            </div>
          </div>
          <div className={note}>{t("clients.forwardsNote")}</div>
        </div>
      </Part>

      {error !== null && error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(error) as TextKey)}</div>
      )}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        {onRemove !== undefined && (
          <button type="button" onClick={onRemove} className={`mr-auto ${danger}`}>
            {t("clients.remove")}
          </button>
        )}
        <button type="button" onClick={onClose} className={secondary}>
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

function Fixed({ caption, value, dash }: { caption: string; value: string; dash: string }) {
  return (
    <div>
      <span className={label}>{caption}</span>
      <div className={`mt-1 truncate ${field}`} title={value}>
        {value.length === 0 ? dash : value}
      </div>
    </div>
  )
}

function isPort(value: number): boolean {
  return Number.isInteger(value) && value > 0 && value <= 65535
}

function portOf(text: string): number {
  const digits = text.replace(/\D/g, "")

  return digits.length === 0 ? 0 : Math.min(Number(digits), 65535)
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
