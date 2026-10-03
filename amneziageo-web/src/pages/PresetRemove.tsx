import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { usePresets, useRemovePreset } from "@/api/presets"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function PresetRemove() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/clients/presets")
  const { presetId } = useParams()
  const presets = usePresets()
  const remove = useRemovePreset()
  const held = presets.data?.find((one) => one.id === Number(presetId))

  useTail(
    held === undefined
      ? []
      : [{ label: held.name, to: `/clients/presets/${held.id}/edit` }, { label: t("presets.remove") }],
  )

  if (held === undefined) {
    return presets.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("presets.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("presets.removeTitle", { name: held.name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        <div className="mt-1 text-[13px] text-muted">{t("presets.removeNote")}</div>
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
          {t("presets.remove")}
        </button>
      </div>
    </div>
  )
}
