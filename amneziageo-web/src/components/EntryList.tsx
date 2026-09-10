import { useState } from "react"
import type { KeyboardEvent } from "react"
import { useGeoEntries, useGeoKeys } from "@/api/geo"
import type { GeoKeys } from "@/api/geo"
import { card, field } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"

const offered = 8

export function EntryList({
  id,
  value,
  placeholder,
  onChange,
}: {
  id: string
  value: string[]
  placeholder: string
  onChange: (value: string[]) => void
}) {
  const t = useText()
  const keys = useGeoKeys()
  const [text, setText] = useState("")
  const [focused, setFocused] = useState(false)
  const [open, setOpen] = useState<string | null>(null)
  const query = text.trim().toLowerCase()
  const typed = entry(text)
  const offers =
    focused && query.length > 0
      ? [
          ...(typed !== null && !value.includes(typed) ? [typed] : []),
          ...geoKeys(keys.data)
            .filter((one) => one.includes(query) && one !== typed && !value.includes(one))
            .sort((a, b) => rank(a, query) - rank(b, query))
            .slice(0, offered),
        ]
      : []

  function add(found: string[]) {
    const fresh = found.filter((one, at) => !value.includes(one) && found.indexOf(one) === at)
    if (fresh.length > 0) {
      onChange([...value, ...fresh])
    }

    setText("")
  }

  function press(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key !== "Enter") {
      return
    }

    event.preventDefault()
    const many = text
      .split(/[\s,;]+/)
      .map((one) => entry(one))
      .filter((one): one is string => one !== null)
    if (many.length > 1) {
      add(many)

      return
    }

    const first = offers[0]
    if (first !== undefined) {
      add([first])
    }
  }

  return (
    <div>
      <div className="relative">
        <input
          id={id}
          value={text}
          placeholder={placeholder}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={press}
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
          className={field}
        />
        {offers.length > 0 && (
          <div className={`absolute z-40 mt-1 flex w-full flex-col py-1 shadow-lg ${card}`}>
            {offers.map((one) => (
              <button
                key={one}
                type="button"
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => add([one])}
                className="flex items-center justify-between gap-3 px-3 py-2 text-left text-sm text-ink hover:bg-hover"
              >
                <span className="truncate font-mono">{one}</span>
                <span className="shrink-0 text-xs text-muted">{t(badge(one))}</span>
              </button>
            ))}
          </div>
        )}
      </div>

      {value.length > 0 && (
        <div className="mt-2 max-h-72 overflow-y-auto rounded border border-line">
          {value.map((one) => (
            <div key={one} className="border-b border-line last:border-b-0">
              <div className="flex items-center gap-3 px-3 py-1.5 text-sm">
                <span className="w-16 shrink-0 text-xs text-muted">{t(badge(one))}</span>
                <span className="min-w-0 flex-1 truncate font-mono text-ink">{one}</span>
                {geo(one) && (
                  <button
                    type="button"
                    title={t("templates.showEntries")}
                    aria-label={t("templates.showEntries")}
                    onClick={() => setOpen(open === one ? null : one)}
                    className="px-1 text-muted hover:text-brand-ink"
                  >
                    {open === one ? "▾" : "▸"}
                  </button>
                )}
                <button
                  type="button"
                  title={t("templates.remove")}
                  aria-label={t("templates.remove")}
                  onClick={() => onChange(value.filter((other) => other !== one))}
                  className="px-1 text-muted hover:text-alarm"
                >
                  &times;
                </button>
              </div>
              {open === one && <Inside token={one} />}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function Inside({ token }: { token: string }) {
  const t = useText()
  const inside = useGeoEntries(token)
  const data = inside.data

  return (
    <div className="px-3 pb-2">
      <div className="text-xs text-muted">
        {data === undefined ? t("templates.loading") : summary(t, data.total, data.entries.length)}
      </div>
      {data !== undefined && data.entries.length > 0 && (
        <pre className="mt-1 max-h-40 overflow-auto rounded bg-canvas p-2 text-xs text-ink">{data.entries.join("\n")}</pre>
      )}
    </div>
  )
}

function summary(t: Text, total: number, shown: number): string {
  if (total === 0) {
    return t("templates.noEntries")
  }

  return shown < total
    ? t("templates.entriesShown", { shown: String(shown), total: String(total) })
    : t("templates.entriesCount", { count: String(total) })
}

function entry(text: string): string | null {
  const body = host(text.trim().replace(/^(domain|cidr):/i, "").trim())
  if (body.length === 0) {
    return null
  }

  if (/^(geoip|geosite):[a-z0-9._-]{1,64}$/i.test(body)) {
    return body.toLowerCase()
  }

  if (/^[\d.]+(\/\d{1,2})?$/.test(body) || (body.includes(":") && /^[\da-f:.]+(\/\d{1,3})?$/i.test(body))) {
    return body
  }

  return /^[\p{L}\p{N}_-]+(\.[\p{L}\p{N}_-]+)+$/u.test(body) ? body.toLowerCase() : null
}

function host(text: string): string {
  const scheme = text.indexOf("://")
  if (scheme < 0) {
    return text
  }

  const rest = text.slice(scheme + 3)
  const cut = rest.search(/[/?#:]/)

  return cut < 0 ? rest : rest.slice(0, cut)
}

function badge(one: string): TextKey {
  if (one.startsWith("geoip:")) {
    return "templates.kindGeoIp"
  }

  if (one.startsWith("geosite:")) {
    return "templates.kindGeoSite"
  }

  if (one.includes("/")) {
    return "templates.kindNetwork"
  }

  return /^[\d.]+$/.test(one) || one.includes(":") ? "templates.kindAddress" : "templates.kindDomain"
}

function geo(one: string): boolean {
  return one.startsWith("geoip:") || one.startsWith("geosite:")
}

function geoKeys(keys: GeoKeys | undefined): string[] {
  return [
    ...(keys?.countries ?? []).map((one) => `geoip:${one.toLowerCase()}`),
    ...(keys?.categories ?? []).map((one) => `geosite:${one.toLowerCase()}`),
  ]
}

function rank(one: string, query: string): number {
  const code = one.slice(one.indexOf(":") + 1)
  if (one === query || code === query) {
    return 0
  }

  return code.startsWith(query) || one.startsWith(query) ? 1 : 2
}
