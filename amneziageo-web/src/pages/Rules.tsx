import { useState } from "react"
import { reason } from "@/api/auth"
import { useOutbounds } from "@/api/outbounds"
import {
  draftOf,
  freshRule,
  useAddRule,
  useChangeRule,
  useMoveRule,
  useRemoveRule,
  useRules,
  useSwitchRule,
} from "@/api/rules"
import type { Rule } from "@/api/rules"
import { scopes } from "@/api/scopes"
import { Modal } from "@/components/Modal"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { RuleForm } from "@/components/RuleForm"
import { card, danger, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Rules() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const rules = useRules()
  const outbounds = useOutbounds()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<Rule | null>(null)
  const [removing, setRemoving] = useState<Rule | null>(null)
  const add = useAddRule()
  const change = useChangeRule()
  const remove = useRemoveRule()
  const move = useMoveRule()
  const turn = useSwitchRule()
  const may = holds(user, scopes.manageRouting)
  const last = (rules.data?.length ?? 0) - 1

  return (
    <div>
      <div className={`mt-4 ${card}`}>
        {may && (
          <div className="flex justify-end gap-2 border-b border-line px-4 py-3">
            <button
              type="button"
              onClick={() => setAdding(true)}
              disabled={(outbounds.data?.length ?? 0) === 0}
              className={primary}
            >
              {t("rules.add")}
            </button>
          </div>
        )}

        {rules.data?.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("rules.empty")}</div>}

        {rules.data && rules.data.length > 0 && (
          <Rows
            items={rules.data}
            keyOf={(rule) => rule.id}
            columns={[
              {
                key: "name",
                caption: t("rules.name"),
                lead: true,
                body: "font-medium text-ink",
                cell: (rule) => (
                  <>
                    {rule.name}
                    {!rule.isEnabled && <span className="ml-2 text-xs text-muted">{t("rules.off")}</span>}
                  </>
                ),
              },
              {
                key: "action",
                caption: t("rules.action"),
                body: "text-muted",
                cell: (rule) => (rule.action === "block" ? t("rules.actionBlock") : rule.outbound),
              },
              {
                key: "targets",
                caption: t("rules.targets"),
                body: "max-w-72 text-muted",
                cell: (rule) => (
                  <span className="block truncate" title={rule.targets.join(", ")}>
                    {rule.targets.length > 0 ? rule.targets.join(", ") : t("rules.anything")}
                  </span>
                ),
              },
              {
                key: "sources",
                caption: t("rules.sources"),
                body: "max-w-48 text-muted",
                cell: (rule) => (
                  <span className="block truncate" title={rule.sources.join(", ")}>
                    {rule.sources.length > 0 ? rule.sources.join(", ") : t("rules.anyone")}
                  </span>
                ),
              },
              { key: "traffic", caption: t("rules.traffic"), body: "text-muted", cell: (rule) => traffic(rule, t) },
              {
                key: "ranges",
                caption: t("rules.ranges"),
                body: "text-muted",
                cell: (rule) => `${rule.state.ranges} / ${rule.state.names}`,
              },
              { key: "state", caption: t("rules.state"), cell: (rule) => <State rule={rule} t={t} /> },
              {
                key: "actions",
                caption: t("rules.actions"),
                tail: true,
                cell: (rule, at) =>
                  may && (
                    <RowActions
                      title={t("rules.actions")}
                      actions={[
                        {
                          label: rule.isEnabled ? t("rules.turnOff") : t("rules.turnOn"),
                          onPick: () => void turn.mutateAsync({ id: rule.id, on: !rule.isEnabled }),
                        },
                        { label: t("rules.edit"), onPick: () => setEditing(rule) },
                        ...(at > 0
                          ? [{ label: t("rules.up"), onPick: () => void move.mutateAsync({ id: rule.id, up: true }) }]
                          : []),
                        ...(at < last
                          ? [{ label: t("rules.down"), onPick: () => void move.mutateAsync({ id: rule.id, up: false }) }]
                          : []),
                        { label: t("rules.remove"), onPick: () => setRemoving(rule), alarming: true },
                      ]}
                    />
                  ),
              },
            ]}
          />
        )}
      </div>

      {adding && (
        <RuleForm
          title={t("rules.newTitle")}
          start={{ ...freshRule, outbound: outbounds.data?.[0]?.name ?? "" }}
          pending={add.isPending}
          error={add.error}
          onSave={(draft) => void add.mutateAsync(draft).then(() => setAdding(false))}
          onClose={() => setAdding(false)}
        />
      )}

      {editing && (
        <RuleForm
          title={t("rules.editTitle", { name: editing.name })}
          start={draftOf(editing)}
          pending={change.isPending}
          error={change.error}
          onSave={(draft) => void change.mutateAsync({ id: editing.id, draft }).then(() => setEditing(null))}
          onClose={() => setEditing(null)}
        />
      )}

      {removing && (
        <Modal
          title={t("rules.removeTitle", { name: removing.name })}
          onClose={() => setRemoving(null)}
          footer={
            <>
              <button type="button" onClick={() => setRemoving(null)} className={secondary}>
                {t("rules.cancel")}
              </button>
              <button
                type="button"
                onClick={() => void remove.mutateAsync(removing.id).then(() => setRemoving(null))}
                disabled={remove.isPending}
                className={danger}
              >
                {t("rules.remove")}
              </button>
            </>
          }
        >
          <div className="text-sm text-muted">{removing.targets.join(", ")}</div>
        </Modal>
      )}
    </div>
  )
}

function State({ rule, t }: { rule: Rule; t: Text }) {
  if (rule.state.fault.length > 0) {
    return (
      <span className="text-alarm" title={rule.state.message}>
        {t(reason(rule.state.fault))}
      </span>
    )
  }

  if (!rule.state.isLive) {
    return <span className="text-muted">{t("rules.off")}</span>
  }

  return (
    <span className="text-ink">
      {t("rules.live")}
    </span>
  )
}

function traffic(rule: Rule, t: Text): string {
  const protocol = rule.protocol === "any" ? t("rules.protocolAny") : rule.protocol.toUpperCase()

  return rule.ports.length > 0 ? `${protocol} ${rule.ports.join(", ")}` : protocol
}
