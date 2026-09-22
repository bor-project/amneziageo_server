import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useGeoSources, useRemoveGeoSource } from "@/api/geo"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function GeoRemove() {
  const t = useText()
  const navigate = useNavigate()
  const { sourceId } = useParams()
  const sources = useGeoSources()
  const remove = useRemoveGeoSource()
  const all = sources.data ?? []
  const held = all.find((one) => one.id === Number(sourceId))

  useTail(
    held === undefined ? [] : [{ label: held.name, to: `/routing/geo/${held.id}/edit` }, { label: t("geo.remove") }],
  )

  if (held === undefined) {
    return sources.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("geo.loading")}</div>
    ) : (
      <Navigate to="/routing/geo" replace />
    )
  }

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("geo.removeTitle", { name: held.name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        <div className="mt-1 text-[13px] break-all text-muted">{held.url}</div>
      </div>

      {remove.error !== null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}

      <div className="flex justify-end gap-2">
        <Link to="/routing/geo" className={`flex h-10 items-center ${secondary}`}>
          {t("action.backToList")}
        </Link>
        <button
          type="button"
          onClick={() => void remove.mutateAsync(held.id).then(() => navigate("/routing/geo"))}
          disabled={remove.isPending}
          className={danger}
        >
          {t("geo.remove")}
        </button>
      </div>
    </div>
  )
}
