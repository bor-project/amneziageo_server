import { useNavigate } from "react-router-dom"
import { useOutbounds } from "@/api/outbounds"
import { draftOf, freshRule, useAddRule, useChangeRule, useRules } from "@/api/rules"
import { RuleForm } from "@/components/RuleForm"
import { RoutingEditor } from "@/components/RoutingEditor"
import { useText } from "@/i18n"

export function RoutingRuleEditor({ id, onClose }: { id: number | null; onClose: () => void }) {
  const t = useText()
  const navigate = useNavigate()
  const rules = useRules()
  const outbounds = useOutbounds()
  const add = useAddRule()
  const change = useChangeRule()
  const held = rules.data?.find((rule) => rule.id === id)

  if (id !== null && held === undefined) {
    return <div className="p-4 text-sm text-muted">{t("rules.loading")}</div>
  }

  const start = held ? draftOf(held) : {
    ...freshRule,
    action: outbounds.data?.length ? "out" as const : "direct" as const,
    outbound: outbounds.data?.[0]?.name ?? "",
  }

  return (
    <RoutingEditor title={t("routing.rule")} onClose={onClose}>
      <RuleForm embedded start={start} self={held?.id} pending={add.isPending || change.isPending}
        error={add.error ?? change.error}
        onSave={(draft) => void (held ? change.mutateAsync({ id: held.id, draft }) : add.mutateAsync(draft))
          .then(onClose).catch(() => undefined)}
        onClose={onClose}
        onRemove={held ? () => navigate("/routing/rules/" + held.id + "/delete") : undefined}
      />
    </RoutingEditor>
  )
}
