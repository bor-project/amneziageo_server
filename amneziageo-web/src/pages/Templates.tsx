import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { scopes } from "@/api/scopes"
import {
  draftOf,
  useAddTemplate,
  useRefreshTemplate,
  useTemplateDefaults,
  useTemplates,
} from "@/api/templates"
import type { Template } from "@/api/templates"
import { RowActions } from "@/components/RowActions"
import type { RowAction } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, fieldBox } from "@/components/styles"
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
  const defaults = useTemplateDefaults().data
  const add = useAddTemplate()
  const refresh = useRefreshTemplate()
  const may = holds(user, scopes.manageClients)
  const find = params.get("find") ?? ""
  const busy = params.get("busy")
  const list = (templates.data ?? []).filter((one) => matches(one, find))
  const allowed = defaults?.allowedIps.join(", ") ?? ""
  const taken = (templates.data ?? []).find((one) => String(one.id) === busy)

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
    const again: RowAction[] =
      one.entries.length > 0 ? [{ label: t("templates.refresh"), onPick: () => refresh.mutate(one.id) }] : []

    return [
      { label: t("templates.edit"), onPick: () => navigate(`/connections/templates/${one.id}/edit`) },
      {
        label: t("templates.duplicate"),
        onPick: () =>
          void add.mutateAsync({ ...draftOf(one), name: t("templates.copyName", { name: one.name }) }).then((made) =>
            navigate(`/connections/templates/${made.id}/edit`),
          ),
      },
      ...again,
      {
        label: t("templates.remove"),
        onPick: () =>
          one.clients > 0 ? put("busy", String(one.id)) : navigate(`/connections/templates/${one.id}/delete`),
        alarming: true,
      },
    ]
  }

  function resolved(one: Template) {
    if (refresh.isPending && refresh.variables === one.id) {
      return t("templates.refreshing")
    }

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
      {taken !== undefined && (
        <div className="flex items-start justify-between gap-4 rounded-[10px] border border-alarm-line bg-alarm-soft px-4 py-3">
          <div className="text-[13px] text-alarm">
            {t("templates.inUse", { name: taken.name, count: String(taken.clients) })}
          </div>
          <button type="button" onClick={() => put("busy", "")} className="text-[13px] text-alarm hover:text-alarm-lit">
            {t("templates.cancel")}
          </button>
        </div>
      )}

      <div className={card}>
        {refresh.error !== null && (
          <div className="border-b border-line px-4 py-2 text-sm text-alarm">{t(complaint(refresh.error))}</div>
        )}

        {add.error !== null && (
          <div className="border-b border-line px-4 py-2 text-sm text-alarm">{t(complaint(add.error))}</div>
        )}

        {(templates.data ?? []).length === 0 && (
          <div className="px-4 py-6 text-sm text-muted">{t("templates.empty")}</div>
        )}

        {(templates.data ?? []).length > 0 && (
          <Rows
            items={list}
            keyOf={(one) => one.id}
            tools={
              <input
                value={find}
                placeholder={t("action.search")}
                onChange={(e) => put("find", e.target.value)}
                className={`w-full wide:w-80 ${fieldBox}`}
              />
            }
            columns={[
              {
                key: "name",
                caption: t("templates.name"),
                sort: (one) => one.name,
                lead: true,
                body: "font-semibold text-ink",
                cell: (one) => (
                  <Link to={`/connections/templates/${one.id}`} className="hover:text-brand-ink">
                    {one.name}
                  </Link>
                ),
              },
              {
                key: "allowed",
                caption: t("templates.allowed"),
                sort: (one) => entries(t, one.entries, allowed),
                body: "max-w-52",
                cell: (one) => (
                  <span className="block truncate" title={one.entries.join(", ")}>
                    {entries(t, one.entries, allowed)}
                  </span>
                ),
              },
              {
                key: "resolved",
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
                caption: t("templates.clients"),
                sort: (one) => one.clients,
                cell: (one) => one.clients,
              },
              {
                key: "actions",
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
