import { Navigate, useNavigate, useParams } from "react-router-dom"
import { draftOf, freshBalancer, useAddBalancer, useBalancers, useChangeBalancer } from "@/api/balancers"
import { BalancerForm } from "@/components/BalancerForm"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { useRelatedReturn } from "@/components/routingReturn"
import { useSpot } from "@/store/spots"

export function BalancerPage() {
  const { balancerId } = useParams()

  return balancerId === undefined ? <NewBalancer /> : <HeldBalancer balancerId={Number(balancerId)} />
}

function NewBalancer() {
  const t = useText()
  const back = useSpot("/routing/balancers")
  const close = useRelatedReturn(back)
  const add = useAddBalancer()

  useTail([{ label: t("balancers.newTitle") }])

  return (
    <BalancerForm
      start={freshBalancer}
      pending={add.isPending}
      error={add.error}
      onSave={(draft) => add.mutateAsync(draft)}
      onSaved={(draft) => close({ outbound: draft.name.trim() })}
      onClose={() => close()}
    />
  )
}

function HeldBalancer({ balancerId }: { balancerId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/balancers")
  const close = useRelatedReturn(back)
  const balancers = useBalancers()
  const change = useChangeBalancer()
  const all = balancers.data ?? []
  const held = all.find((one) => one.id === balancerId)

  useTail(held === undefined ? [] : [{ label: held.name }])

  if (held === undefined) {
    return balancers.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("outbounds.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  return (
    <BalancerForm
      key={held.id}
      start={draftOf(held)}
      self={held.name}
      pending={change.isPending}
      error={change.error}
      onSave={(draft) => change.mutateAsync({ id: held.id, draft })}
      onSaved={(draft) => close({ outbound: draft.name.trim() })}
      onClose={() => close()}
      onRemove={() => navigate(`/routing/balancers/${held.id}/delete`)}
    />
  )
}
