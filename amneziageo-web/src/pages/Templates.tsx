import { useState } from "react"
import { useNavigate } from "react-router-dom"
import { complaint } from "@/api/auth"
import { scopes } from "@/api/scopes"
import { useRefreshTemplate, useRemoveTemplate, useTemplateDefaults, useTemplates } from "@/api/templates"
import type { Template } from "@/api/templates"
import { Modal } from "@/components/Modal"
import { RowActions } from "@/components/RowActions"
import type { RowAction } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, danger, primary, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Templates() {
  const t = useText()
  const language = useLanguage()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const templates = useTemplates()
  const defaults = useTemplateDefaults().data
  const [removing, setRemoving] = useState<Template | null>(null)
  const refresh = useRefreshTemplate()
  const remove = useRemoveTemplate()
  const may = holds(user, scopes.manageClients)
  const list = templates.data ?? []
  const allowed = defaults?.allowedIps.join(", ") ?? ""

  function drop() {
    remove.reset()
    setRemoving(null)
  }

  function actions(one: Template): RowAction[] {
    const again: RowAction[] =
      one.entries.length > 0 ? [{ label: t("templates.refresh"), onPick: () => refresh.mutate(one.id) }] : []

    return [
      ...again,
      { label: t("templates.edit"), onPick: () => navigate(`/connections/templates/${one.id}`) },
      { label: t("templates.remove"), onPick: () => setRemoving(one), alarming: true },
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
    <div>
      <div className={`mt-4 ${card}`}>
        {may && (
          <div className="flex justify-end border-b border-line px-4 py-3">
            <button type="button" onClick={() => navigate("/connections/templates/new")} className={primary}>
              {t("templates.add")}
            </button>
          </div>
        )}

        {refresh.error !== null && (
          <div className="border-b border-line px-4 py-2 text-sm text-alarm">{t(complaint(refresh.error))}</div>
        )}

        {list.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("templates.empty")}</div>}

        {list.length > 0 && (
          <Rows
            items={list}
            keyOf={(one) => one.id}
            columns={[
              {
                key: "name",
                caption: t("templates.name"),
                sort: (one) => one.name,
                lead: true,
                body: "font-medium text-ink",
                cell: (one) => one.name,
              },
              {
                key: "allowed",
                caption: t("templates.allowed"),
                sort: (one) => entries(t, one.entries, allowed),
                body: "max-w-52 text-muted",
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
                body: "text-muted",
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
                key: "dns",
                caption: t("templates.dns"),
                sort: (one) => listed(one.dns, defaults?.dns),
                body: "text-muted",
                cell: (one) => listed(one.dns, defaults?.dns),
              },
              {
                key: "mtu",
                caption: t("templates.mtu"),
                sort: (one) => one.mtu ?? defaults?.mtu ?? null,
                body: "text-muted",
                cell: (one) => one.mtu ?? defaults?.mtu,
              },
              {
                key: "keepalive",
                caption: t("templates.keepalive"),
                sort: (one) => one.keepalive ?? defaults?.keepalive ?? null,
                body: "text-muted",
                cell: (one) => one.keepalive ?? defaults?.keepalive,
              },
              {
                key: "clients",
                caption: t("templates.clients"),
                sort: (one) => one.clients,
                body: "text-muted",
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

      {removing && (
        <Modal
          title={t("templates.removeTitle", { name: removing.name })}
          onClose={drop}
          footer={
            <>
              <button type="button" onClick={drop} className={secondary}>
                {t("templates.cancel")}
              </button>
              <button
                type="button"
                onClick={() => void remove.mutateAsync(removing.id).then(drop)}
                disabled={remove.isPending}
                className={danger}
              >
                {t("templates.remove")}
              </button>
            </>
          }
        >
          <div className="text-sm text-muted">{entries(t, removing.entries, allowed)}</div>
          {remove.error !== null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}
        </Modal>
      )}
    </div>
  )
}

function entries(t: Text, values: string[], fallback: string): string {
  if (values.length === 0) {
    return fallback
  }

  const head = values.slice(0, 3).join(", ")

  return values.length > 3 ? `${head} ${t("templates.more", { count: String(values.length - 3) })}` : head
}

function listed(values: string[], fallback: string[] = []): string {
  return (values.length > 0 ? values : fallback).join(", ")
}
