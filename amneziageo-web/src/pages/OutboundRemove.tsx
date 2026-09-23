import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useOutbounds, useRemoveOutbound } from "@/api/outbounds"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function OutboundRemove() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/channels")
  const { outboundId } = useParams()
  const outbounds = useOutbounds()
  const remove = useRemoveOutbound()
  const all = outbounds.data ?? []
  const held = all.find((one) => one.id === Number(outboundId))

  useTail(
    held === undefined
      ? []
      : [{ label: held.name, to: `/routing/channels/${held.id}/edit` }, { label: t("outbounds.remove") }],
  )

  if (held === undefined) {
    return outbounds.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("outbounds.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("outbounds.removeTitle", { name: held.name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        <div className="mt-1 text-[13px] text-muted">
          {held.kind === "local" ? t("outbounds.kindLocal") : `${held.host}:${held.port}`}
        </div>
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
          {t("outbounds.remove")}
        </button>
      </div>
    </div>
  )
}
