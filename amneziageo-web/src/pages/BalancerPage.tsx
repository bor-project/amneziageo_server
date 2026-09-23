import { Navigate, useNavigate, useParams } from "react-router-dom"
import { draftOf, freshBalancer, useAddBalancer, useBalancers, useChangeBalancer } from "@/api/balancers"
import { BalancerForm } from "@/components/BalancerForm"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function BalancerPage() {
  const { balancerId } = useParams()

  return balancerId === undefined ? <NewBalancer /> : <HeldBalancer balancerId={Number(balancerId)} />
}

function NewBalancer() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/channels")
  const add = useAddBalancer()

  useTail([{ label: t("balancers.newTitle") }])

  return (
    <BalancerForm
      start={freshBalancer}
      pending={add.isPending}
      error={add.error}
      onSave={(draft) => void add.mutateAsync(draft).then(() => navigate(back))}
      onClose={() => navigate(back)}
    />
  )
}

function HeldBalancer({ balancerId }: { balancerId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/channels")
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
      start={draftOf(held)}
      pending={change.isPending}
      error={change.error}
      onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(back))}
      onClose={() => navigate(back)}
      onRemove={() => navigate(`/routing/channels/groups/${held.id}/delete`)}
    />
  )
}
