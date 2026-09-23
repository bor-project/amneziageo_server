import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useBalancers, useRemoveBalancer } from "@/api/balancers"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function BalancerRemove() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/channels")
  const { balancerId } = useParams()
  const balancers = useBalancers()
  const remove = useRemoveBalancer()
  const all = balancers.data ?? []
  const held = all.find((one) => one.id === Number(balancerId))

  useTail(
    held === undefined
      ? []
      : [{ label: held.name, to: `/routing/channels/groups/${held.id}/edit` }, { label: t("balancers.remove") }],
  )

  if (held === undefined) {
    return balancers.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("outbounds.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("balancers.removeTitle", { name: held.name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        <div className="mt-1 text-[13px] text-muted">{held.members.join(", ")}</div>
      </div>

      {remove.error !== null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}

      <div className="flex justify-end gap-2">
        <Link to={back} className={`flex h-10 items-center ${secondary}`}>
          {t("action.backToList")}
        </Link>
        <button
          type="button"
          onClick={() => void remove.mutateAsync(held.id).then(() => navigate(back))}
          disabled={remove.isPending}
          className={danger}
        >
          {t("balancers.remove")}
        </button>
      </div>
    </div>
  )
}
