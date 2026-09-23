import { Navigate, useNavigate, useParams } from "react-router-dom"
import { useOutbounds } from "@/api/outbounds"
import { draftOf, freshRule, useAddRule, useChangeRule, useRules } from "@/api/rules"
import { RuleForm } from "@/components/RuleForm"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function RulePage() {
  const { ruleId } = useParams()

  return ruleId === undefined ? <NewRule /> : <HeldRule ruleId={Number(ruleId)} />
}

function NewRule() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/rules")
  const outbounds = useOutbounds()
  const add = useAddRule()

  useTail([{ label: t("rules.newTitle") }])

  return (
    <RuleForm
      start={{ ...freshRule, outbound: outbounds.data?.[0]?.name ?? "" }}
      pending={add.isPending}
      error={add.error}
      onSave={(draft) => void add.mutateAsync(draft).then(() => navigate(back))}
      onClose={() => navigate(back)}
    />
  )
}

function HeldRule({ ruleId }: { ruleId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/rules")
  const rules = useRules()
  const change = useChangeRule()
  const all = rules.data ?? []
  const held = all.find((one) => one.id === ruleId)

  useTail(held === undefined ? [] : [{ label: held.name }])

  if (held === undefined) {
    return rules.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("rules.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  return (
    <RuleForm
      start={draftOf(held)}
      pending={change.isPending}
      error={change.error}
      onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(back))}
      onClose={() => navigate(back)}
      onRemove={() => navigate(`/routing/rules/${held.id}/delete`)}
    />
  )
}
