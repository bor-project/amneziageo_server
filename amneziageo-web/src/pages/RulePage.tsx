import { Navigate, useLocation, useNavigate, useParams } from "react-router-dom"
import { trafficDraft, useAddTrafficRule, useChangeTrafficRule, useFreshTrafficRule, useTrafficRules } from "@/api/trafficRules"
import { complaint } from "@/api/auth"
import { RuleForm } from "@/components/RuleForm"
import { useTail } from "@/components/crumbs"
import type { RoutingPageState } from "@/components/routingReturn"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function RulePage() {
  const { ruleId } = useParams()
  return ruleId === undefined ? <NewRule /> : <HeldRule ruleId={Number(ruleId)} />
}

function useAddressReturn() {
  const navigate = useNavigate()
  const back = useSpot("/routing/addresses")
  const returning = (useLocation().state as RoutingPageState | null)?.entryReturn

  return (added?: number) => {
    if (returning) {
      const ruleIds = added !== undefined && !returning.draft.ruleIds.includes(added) ? [...returning.draft.ruleIds, added] : returning.draft.ruleIds
      navigate(returning.to, { state: { entryResume: { ...returning.draft, ruleIds } } satisfies RoutingPageState })
    } else {
      navigate(back)
    }
  }
}

function NewRule() {
  const t = useText()
  const back = useAddressReturn()
  const fresh = useFreshTrafficRule()
  const add = useAddTrafficRule()
  const returning = (useLocation().state as RoutingPageState | null)?.entryReturn
  useTail([{ label: t("routing.newAddressSet") }])

  if (fresh.data === undefined) {
    return <div className="mt-4 text-sm text-muted">{t(fresh.error ? complaint(fresh.error) : "rules.loading")}</div>
  }

  return <RuleForm start={{ ...fresh.data, dnsMode: fresh.data.dnsMode ?? "default", dnsMembers: fresh.data.dnsMembers ?? {} }} returning={returning} pending={add.isPending} error={add.error}
    onSave={(draft) => void add.mutateAsync(draft).then((answer) => back(answer.data.id)).catch(() => undefined)} onClose={() => back()} />
}

function HeldRule({ ruleId }: { ruleId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const back = useAddressReturn()
  const rules = useTrafficRules()
  const change = useChangeTrafficRule()
  const returning = (useLocation().state as RoutingPageState | null)?.entryReturn
  const held = rules.data?.find((one) => one.id === ruleId)
  useTail(held === undefined ? [] : [{ label: held.name }])

  if (held === undefined) {
    return rules.data === undefined ? <div className="mt-4 text-sm text-muted">{t("rules.loading")}</div> : <Navigate to="/routing/addresses" replace />
  }

  return <RuleForm key={held.id} start={trafficDraft(held)} self={held.id} returning={returning} pending={change.isPending} error={change.error}
    onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => back()).catch(() => undefined)}
    onClose={() => back()} onRemove={() => navigate(`/routing/addresses/${held.id}/delete`)} />
}
