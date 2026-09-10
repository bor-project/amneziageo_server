import { useState } from "react"
import { complaint } from "@/api/auth"
import { scopes } from "@/api/scopes"
import {
  draftOf,
  freshTemplate,
  useAddTemplate,
  useChangeTemplate,
  useRefreshTemplate,
  useRemoveTemplate,
  useTemplateDefaults,
  useTemplates,
} from "@/api/templates"
import type { Template } from "@/api/templates"
import { Modal } from "@/components/Modal"
import { RowActions } from "@/components/RowActions"
import type { RowAction } from "@/components/RowActions"
import { TemplateForm } from "@/components/TemplateForm"
import { card, danger, primary, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Templates() {
  const t = useText()
  const language = useLanguage()
  const user = useAppSelector((s) => s.auth.user)
  const templates = useTemplates()
  const defaults = useTemplateDefaults().data
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<Template | null>(null)
  const [removing, setRemoving] = useState<Template | null>(null)
  const add = useAddTemplate()
  const change = useChangeTemplate()
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
      { label: t("templates.edit"), onPick: () => setEditing(one) },
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
            <button type="button" onClick={() => setAdding(true)} className={primary}>
              {t("templates.add")}
            </button>
          </div>
        )}

        {refresh.error !== null && (
          <div className="border-b border-line px-4 py-2 text-sm text-alarm">{t(complaint(refresh.error))}</div>
        )}

        {list.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("templates.empty")}</div>}

        {list.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-xs text-muted">
                <tr>
                  <th className="px-4 py-2 font-normal">{t("templates.name")}</th>
                  <th className="px-4 py-2 font-normal">{t("templates.allowed")}</th>
                  <th className="px-4 py-2 font-normal">{t("templates.resolved")}</th>
                  <th className="px-4 py-2 font-normal">{t("templates.dns")}</th>
                  <th className="px-4 py-2 font-normal">{t("templates.mtu")}</th>
                  <th className="px-4 py-2 font-normal">{t("templates.keepalive")}</th>
                  <th className="px-4 py-2 font-normal">{t("templates.clients")}</th>
                  <th className="px-4 py-2" />
                </tr>
              </thead>
              <tbody>
                {list.map((one) => (
                  <tr key={one.id} className="border-t border-line">
                    <td className="px-4 py-2 font-medium text-ink">{one.name}</td>
                    <td className="max-w-52 truncate px-4 py-2 text-muted" title={one.entries.join(", ")}>
                      {entries(t, one.entries, allowed)}
                    </td>
                    <td
                      className="px-4 py-2 text-muted"
                      title={
                        one.refreshedUtc === null
                          ? undefined
                          : t("templates.refreshed", { time: new Date(one.refreshedUtc).toLocaleString(language) })
                      }
                    >
                      {resolved(one)}
                    </td>
                    <td className="px-4 py-2 text-muted">{listed(one.dns, defaults?.dns)}</td>
                    <td className="px-4 py-2 text-muted">{one.mtu ?? defaults?.mtu}</td>
                    <td className="px-4 py-2 text-muted">{one.keepalive ?? defaults?.keepalive}</td>
                    <td className="px-4 py-2 text-muted">{one.clients}</td>
                    <td className="px-4 py-2">
                      {may && <RowActions title={t("templates.actions")} actions={actions(one)} />}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {adding && (
        <TemplateForm
          title={t("templates.newTitle")}
          start={freshTemplate}
          pending={add.isPending}
          error={add.error}
          onSave={(draft) => void add.mutateAsync(draft).then(() => setAdding(false))}
          onClose={() => setAdding(false)}
        />
      )}

      {editing && (
        <TemplateForm
          title={t("templates.editTitle", { name: editing.name })}
          start={draftOf(editing)}
          held={editing}
          pending={change.isPending}
          error={change.error}
          onSave={(draft) => void change.mutateAsync({ id: editing.id, draft }).then(() => setEditing(null))}
          onClose={() => setEditing(null)}
        />
      )}

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
