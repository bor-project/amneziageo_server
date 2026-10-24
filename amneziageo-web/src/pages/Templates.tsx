import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { findingsOf, useDnsConflicts } from "@/api/dns"
import { useFailure } from "@/api/failure"
import { usePresets } from "@/api/presets"
import { scopes } from "@/api/scopes"
import { useTemplateDefaults, useTemplates } from "@/api/templates"
import type { Template } from "@/api/templates"
import { RowActions } from "@/components/RowActions"
import type { RowAction } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { DnsMark } from "@/components/DnsMark"
import { Find } from "@/components/fields"
import { alarmBar, alarmLine, card, fieldBox } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Templates() {
  const t = useText()
  const language = useLanguage()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const [params, setParams] = useSearchParams()
  const templates = useTemplates()
  const failed = useFailure(templates)
  const defaults = useTemplateDefaults().data
  const may = holds(user, scopes.manageClients)
  const report = useDnsConflicts().data
  const presets = usePresets().data ?? []
  const find = params.get("find") ?? ""
  const preset = params.get("preset") ?? ""
  const all = templates.data ?? []
  const list = all.filter((one) => matches(one, find) && (preset === "" || one.presets.includes(Number(preset))))
  const allowed = defaults?.allowedIps.join(", ") ?? ""
  const plain: Template | null =
    defaults === undefined
      ? null
      : {
          id: 0,
          name: t("clients.noTemplate"),
          entries: [],
          allowedIps: defaults.allowedIps,
          missed: [],
          dnsMode: "auto",
          dns: defaults.dns,
          mtu: defaults.mtu,
          keepalive: defaults.keepalive,
          presets: [],
          presetsOnly: false,
          standIns: false,
          clients: defaults.clientIds.length,
          clientIds: defaults.clientIds,
          refreshedUtc: null,
          createdUtc: "",
          updatedUtc: "",
        }

  function put(key: string, value: string) {
    const kept = new URLSearchParams(params)

    if (value === "") {
      kept.delete(key)
    } else {
      kept.set(key, value)
    }

    setParams(kept, { replace: true })
  }

  function actions(one: Template): RowAction[] {
    return [{ label: t("action.settings"), onPick: () => navigate(pageOf(one)) }]
  }

  function resolved(one: Template) {
    if (one.entries.length === 0) {
      return t("clients.dash")
    }

    return (
      <>
        {one.allowedIps.length}
        {one.missed.length > 0 && (
          <div className="text-xs whitespace-nowrap text-warn">
            {t("templates.missedShort", { count: String(one.missed.length) })}
          </div>
        )}
      </>
    )
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <div className={card}>
        {templates.isPending && failed === null && (
          <div className="px-4 py-6 text-sm text-muted">{t("templates.loading")}</div>
        )}
        {failed !== null && (
          <div className={all.length === 0 ? alarmLine : alarmBar}>{t(complaint(failed))}</div>
        )}
        {templates.isSuccess && all.length === 0 && (
          <div className="px-4 py-6 text-sm text-muted">{t("templates.empty")}</div>
        )}

        {all.length > 0 && (
          <Rows
            items={list}
            pinned={plain !== null && preset === "" && matches(plain, find) ? [plain] : []}
            keyOf={(one) => one.id}
            tools={
              <div className="flex flex-wrap items-center gap-2">
                <select
                  id="template-preset"
                  aria-label={t("templates.presets")}
                  value={preset}
                  onChange={(e) => put("preset", e.target.value)}
                  className={`w-full wide:w-56 ${fieldBox}`}
                >
                  <option value="">{t("templates.everyPreset")}</option>
                  {presets.map((one) => (
                    <option key={one.id} value={one.id}>
                      {one.name}
                    </option>
                  ))}
                </select>

                <Find value={find} onChange={(value) => put("find", value)} className="w-full wide:w-72" />
              </div>
            }
            columns={[
              {
                key: "name",
                width: 200,
                caption: t("templates.name"),
                sort: (one) => one.name,
                lead: true,
                body: "font-semibold text-ink",
                cell: (one) => (
                  <span className="flex min-w-0 items-center gap-2">
                    {may ? (
                      <Link
                        to={pageOf(one)}
                        className={`truncate hover:text-brand-ink ${one.id === 0 ? "font-normal text-muted" : ""}`}
                      >
                        {one.name}
                      </Link>
                    ) : (
                      <span className={`truncate ${one.id === 0 ? "font-normal text-muted" : ""}`}>{one.name}</span>
                    )}
                    {one.id > 0 && (
                      <DnsMark findings={findingsOf(report, "template", one.id)} view="template" members={one.clientIds} />
                    )}
                  </span>
                ),
              },
              {
                key: "allowed",
                wrap: true,
                caption: t("templates.allowed"),
                sort: (one) => entries(t, one.entries, allowed),
                cell: (one) => (
                  <span className="block truncate" title={one.entries.join(", ")}>
                    {entries(t, one.entries, allowed)}
                  </span>
                ),
              },
              {
                key: "resolved",
                width: 128,
                caption: t("templates.resolved"),
                sort: (one) => (one.entries.length === 0 ? null : one.allowedIps.length),
                cell: (one) => (
                  <span
                    title={
                      one.refreshedUtc === null
                        ? undefined
                        : t("templates.refreshed", { time: new Date(one.refreshedUtc).toLocaleString(language) })
                    }
                  >
                    {resolved(one)}
                  </span>
                ),
              },
              {
                key: "clients",
                width: 120,
                caption: t("templates.clients"),
                sort: (one) => one.clients,
                cell: (one) => (
                  <Link
                    to={`/clients?template=${one.id === 0 ? "none" : one.id}`}
                    className="text-brand-ink hover:text-brand-lit"
                  >
                    {one.clients}
                  </Link>
                ),
              },
              {
                key: "actions",
                width: 56,
                caption: t("templates.actions"),
                tail: true,
                cell: (one) => may && <RowActions title={t("templates.actions")} actions={actions(one)} />,
              },
            ]}
          />
        )}
      </div>
    </div>
  )
}

function pageOf(one: Template): string {
  return one.id === 0 ? "/clients/templates/default" : `/clients/templates/${one.id}/edit`
}

function matches(one: Template, find: string): boolean {
  const query = find.trim().toLowerCase()

  return (
    query === "" ||
    one.name.toLowerCase().includes(query) ||
    one.entries.some((entry) => entry.toLowerCase().includes(query))
  )
}

function entries(t: Text, values: string[], fallback: string): string {
  if (values.length === 0) {
    return fallback
  }

  const head = values.slice(0, 3).join(", ")

  return values.length > 3 ? `${head} ${t("templates.more", { count: String(values.length - 3) })}` : head
}
