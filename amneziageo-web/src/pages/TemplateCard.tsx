import { Link, Navigate, useParams, useSearchParams } from "react-router-dom"
import { useClients } from "@/api/clients"
import type { Client } from "@/api/clients"
import { scopes } from "@/api/scopes"
import { useRefreshTemplate, useTemplateDefaults, useTemplates } from "@/api/templates"
import type { Template, TemplateDefaults } from "@/api/templates"
import { Rows } from "@/components/Rows"
import { TextBlock } from "@/components/TextBlock"
import { useTail } from "@/components/crumbs"
import { Box } from "@/components/fields"
import { card, chip, primary, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import type { Text } from "@/i18n"
import { templateTrail } from "@/pages/trails"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

const parts = ["params", "clients", "config"] as const

type Part = (typeof parts)[number]

const tab = "-mb-px shrink-0 border-b-2 px-3.5 py-2.5 text-sm"

const captions: Record<Part, "templates.tabParams" | "templates.tabClients" | "templates.tabConfig"> = {
  params: "templates.tabParams",
  clients: "templates.tabClients",
  config: "templates.tabConfig",
}

export function TemplateCard() {
  const t = useText()
  const language = useLanguage()
  const user = useAppSelector((s) => s.auth.user)
  const { templateId } = useParams()
  const [params, setParams] = useSearchParams()
  const templates = useTemplates()
  const defaults = useTemplateDefaults().data
  const clients = useClients()
  const refresh = useRefreshTemplate()
  const held = templates.data?.find((one) => one.id === Number(templateId))
  const part = shown(params.get("tab"))
  const may = holds(user, scopes.manageClients)

  useTail(
    held === undefined
      ? []
      : [templateTrail(held, templates.data ?? []), ...(part === "params" ? [] : [{ label: t(captions[part]) }])],
  )

  function open(next: Part) {
    const kept = new URLSearchParams(params)

    if (next === "params") {
      kept.delete("tab")
    } else {
      kept.set("tab", next)
    }

    setParams(kept, { replace: true })
  }

  if (held === undefined) {
    return templates.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("templates.loading")}</div>
    ) : (
      <Navigate to="/connections/templates" replace />
    )
  }

  const mine = (clients.data ?? []).filter((one) => one.templateId === held.id)

  return (
    <div className="mt-4 flex flex-col gap-5">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <h2 className="truncate text-2xl leading-10 font-semibold tracking-[-0.02em]">{held.name}</h2>
          <div className="text-[13px] text-muted">{meta(t, held, language)}</div>
        </div>

        {may && (
          <div className="flex shrink-0 items-center gap-2">
            {held.entries.length > 0 && (
              <button
                type="button"
                onClick={() => refresh.mutate(held.id)}
                disabled={refresh.isPending}
                className={secondary}
              >
                {refresh.isPending ? t("templates.refreshing") : t("templates.refresh")}
              </button>
            )}
            <Link to="edit" className={`flex h-10 items-center ${primary}`}>
              {t("templates.edit")}
            </Link>
          </div>
        )}
      </div>

      <div className="flex gap-4 overflow-x-auto overflow-y-hidden border-b border-line [scrollbar-width:none] sm:gap-6">
        {parts.map((one) => (
          <button
            key={one}
            type="button"
            onClick={() => open(one)}
            className={`${tab} ${one === part ? "border-brand font-medium text-ink" : "border-transparent text-muted hover:text-ink"}`}
          >
            {t(captions[one])}
          </button>
        ))}
      </div>

      {part === "params" && <Params held={held} defaults={defaults} />}

      {part === "clients" && (
        <div className={card}>
          {mine.length === 0 ? (
            <div className="px-4 py-6 text-sm text-muted">{t("templates.noClients")}</div>
          ) : (
            <Rows
              name="tpl"
              items={mine}
              keyOf={(one) => one.id}
              columns={[
                {
                  key: "name",
                  caption: t("clients.name"),
                  sort: (one: Client) => one.name,
                  lead: true,
                  body: "font-semibold text-ink",
                  cell: (one: Client) => one.name,
                },
                {
                  key: "address",
                  caption: t("clients.address"),
                  sort: (one: Client) => one.address.join(", "),
                  cell: (one: Client) => one.address.join(", "),
                },
                {
                  key: "state",
                  caption: t("clients.enabled"),
                  sort: (one: Client) => (one.state.isOnline ? 0 : 1),
                  cell: (one: Client) =>
                    one.state.isOnline ? <span className="text-good">{t("clients.online")}</span> : t("clients.dash"),
                },
              ]}
            />
          )}
        </div>
      )}

      {part === "config" && (
        <div className={card}>
          <TextBlock className="max-h-[32rem] overflow-auto p-4 font-mono text-xs leading-7 text-mono">
            {file(held, defaults)}
          </TextBlock>
        </div>
      )}
    </div>
  )
}

function Params({ held, defaults }: { held: Template; defaults: TemplateDefaults | undefined }) {
  const t = useText()

  return (
    <div className="grid gap-3.5 [grid-template-columns:repeat(auto-fit,minmax(210px,1fr))]">
      <Box caption={t("templates.allowed")}>
        {held.entries.length === 0 ? (
          <div className="text-[15px] text-body">{defaults?.allowedIps.join(", ")}</div>
        ) : (
          <>
            <div className="flex flex-wrap gap-1.5">
              {held.entries.map((one) => (
                <span key={one} className={chip}>
                  {one}
                </span>
              ))}
            </div>
            <div className="mt-2 text-xs text-faint">
              {t("templates.addressesCount", { count: String(held.allowedIps.length) })}
            </div>
            {held.missed.length > 0 && (
              <div className="mt-1 text-xs text-warn">{t("templates.missed", { list: held.missed.join(", ") })}</div>
            )}
          </>
        )}
      </Box>

      <Box caption={t("templates.dns")}>
        <div className="text-[15px] text-body">{listed(held.dns, defaults?.dns)}</div>
      </Box>

      <Box caption={t("templates.mtu")}>
        <div className="text-[15px] text-body">{held.mtu ?? defaults?.mtu}</div>
      </Box>

      <Box caption={t("templates.keepalive")}>
        <div className="text-[15px] text-body">{held.keepalive ?? defaults?.keepalive}</div>
      </Box>

      <Box caption={t("templates.clients")}>
        <div className="text-[15px] text-body">{held.clients}</div>
      </Box>
    </div>
  )
}

function meta(t: Text, held: Template, language: string): string {
  const count = t("templates.addressesCount", { count: String(held.allowedIps.length) })

  return held.refreshedUtc === null
    ? count
    : `${count} · ${t("templates.refreshed", { time: new Date(held.refreshedUtc).toLocaleString(language) })}`
}

function file(held: Template, defaults: TemplateDefaults | undefined): string {
  const allowed = held.allowedIps.length > 0 ? held.allowedIps : (defaults?.allowedIps ?? [])

  return [
    `AllowedIPs = ${allowed.join(", ")}`,
    `DNS = ${listed(held.dns, defaults?.dns)}`,
    `MTU = ${held.mtu ?? defaults?.mtu ?? ""}`,
    `PersistentKeepalive = ${held.keepalive ?? defaults?.keepalive ?? ""}`,
  ].join("\n")
}

function listed(values: string[], fallback: string[] = []): string {
  return (values.length > 0 ? values : fallback).join(", ")
}

function shown(value: string | null): Part {
  return parts.find((one) => one === value) ?? "params"
}
