import { Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { draftOf, freshPreset, useAddPreset, useChangePreset, usePresets } from "@/api/presets"
import { PresetForm } from "@/components/PresetForm"
import { HeadActions } from "@/components/Section"
import { useTail } from "@/components/crumbs"
import { secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function PresetPage() {
  const t = useText()
  const navigate = useNavigate()
  const { presetId } = useParams()
  const presets = usePresets()
  const add = useAddPreset()
  const change = useChangePreset()
  const held = presetId === undefined ? undefined : presets.data?.find((one) => one.id === Number(presetId))
  const back = useSpot("/clients/presets")

  useTail(held === undefined ? [{ label: t("presets.newTitle") }] : [{ label: held.name }])

  if (presetId === undefined) {
    return (
      <PresetForm
        start={freshPreset}
        pending={add.isPending}
        error={add.error}
        onSave={(draft) => void add.mutateAsync(draft).then(() => navigate(back))}
        onClose={() => navigate(back)}
      />
    )
  }

  if (held === undefined) {
    return presets.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("presets.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  return (
    <div>
      <HeadActions>
        <button
          type="button"
          onClick={() =>
            void add
              .mutateAsync({ ...draftOf(held), name: t("templates.copyName", { name: held.name }) })
              .then((made) => navigate(`/clients/presets/${made.id}/edit`))
          }
          disabled={add.isPending}
          className={`flex h-10 items-center ${secondary}`}
        >
          {t("templates.duplicate")}
        </button>
      </HeadActions>

      {add.error !== null && <div className="mt-2 text-sm text-alarm">{t(complaint(add.error))}</div>}

      <PresetForm
        start={draftOf(held)}
        held={held}
        pending={change.isPending}
        error={change.error}
        onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(back))}
        onClose={() => navigate(back)}
        onRemove={() => navigate(`/clients/presets/${held.id}/delete`)}
      />
    </div>
  )
}
