import { useState } from "react"
import { reason } from "@/api/auth"
import { useOutbounds } from "@/api/outbounds"
import {
  draftOf,
  freshRule,
  useAddRule,
  useApplyRules,
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
  const apply = useApplyRules()
  const may = holds(user, scopes.manageRouting)
  const last = (rules.data?.length ?? 0) - 1

  return (
    <div>
      <div className={`mt-4 ${card}`}>
        {may && (
          <div className="flex justify-end gap-2 border-b border-line px-4 py-3">
            <button
              type="button"
              onClick={() => void apply.mutateAsync()}
              disabled={apply.isPending}
              className={secondary}
            >
              {apply.isPending ? t("rules.applying") : t("rules.apply")}
            </button>
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
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-xs text-muted">
                <tr>
                  <th className="px-4 py-2 font-normal">{t("rules.name")}</th>
                  <th className="px-4 py-2 font-normal">{t("rules.action")}</th>
                  <th className="px-4 py-2 font-normal">{t("rules.targets")}</th>
                  <th className="px-4 py-2 font-normal">{t("rules.sources")}</th>
                  <th className="px-4 py-2 font-normal">{t("rules.traffic")}</th>
                  <th className="px-4 py-2 font-normal">{t("rules.ranges")}</th>
                  <th className="px-4 py-2 font-normal">{t("rules.state")}</th>
                  <th className="px-4 py-2" />
                </tr>
              </thead>
              <tbody>
                {rules.data.map((rule, at) => (
                  <tr key={rule.id} className="border-t border-line">
                    <td className="px-4 py-2 font-medium text-ink">
                      {rule.name}
                      {!rule.isEnabled && <span className="ml-2 text-xs text-muted">{t("rules.off")}</span>}
                    </td>
                    <td className="px-4 py-2 text-muted">
                      {rule.action === "block" ? t("rules.actionBlock") : rule.outbound}
                    </td>
                    <td className="max-w-72 truncate px-4 py-2 text-muted" title={rule.targets.join(", ")}>
                      {rule.targets.length > 0 ? rule.targets.join(", ") : t("rules.anything")}
                    </td>
                    <td className="max-w-48 truncate px-4 py-2 text-muted" title={rule.sources.join(", ")}>
                      {rule.sources.length > 0 ? rule.sources.join(", ") : t("rules.anyone")}
                    </td>
                    <td className="px-4 py-2 text-muted">{traffic(rule, t)}</td>
                    <td className="px-4 py-2 text-muted">{rule.state.ranges} / {rule.state.names}</td>
                    <td className="px-4 py-2">
                      <State rule={rule} t={t} />
                    </td>
                    <td className="px-4 py-2">
                      {may && (
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
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
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
