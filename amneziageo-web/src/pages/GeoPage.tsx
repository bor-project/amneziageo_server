import { Navigate, useNavigate, useParams } from "react-router-dom"
import { draftOf, useAddGeoSource, useChangeGeoSource, useGeoSources, useUpdateGeoSource } from "@/api/geo"
import type { GeoSourceDraft } from "@/api/geo"
import { GeoForm } from "@/components/GeoForm"
import { secondary } from "@/components/styles"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

const fresh: GeoSourceDraft = { name: "", kind: "geoip", url: "", isEnabled: true }

export function GeoPage() {
  const { sourceId } = useParams()

  return sourceId === undefined ? <NewSource /> : <HeldSource sourceId={Number(sourceId)} />
}

function NewSource() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/geo")
  const add = useAddGeoSource()

  useTail([{ label: t("geo.newTitle") }])

  return (
    <GeoForm
      start={fresh}
      pending={add.isPending}
      error={add.error}
      onSave={(draft) => void add.mutateAsync(draft).then(() => navigate(back))}
      onClose={() => navigate(back)}
    />
  )
}

function HeldSource({ sourceId }: { sourceId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/geo")
  const sources = useGeoSources()
  const change = useChangeGeoSource()
  const update = useUpdateGeoSource()
  const all = sources.data ?? []
  const held = all.find((one) => one.id === sourceId)

  useTail(held === undefined ? [] : [{ label: held.name }])

  if (held === undefined) {
    return sources.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("geo.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  return (
    <div>
      <div className="mt-4 flex justify-end">
        <button
          type="button"
          onClick={() => void update.mutateAsync(held.id)}
          disabled={update.isPending}
          className={secondary}
        >
          {update.isPending ? t("geo.updating") : t("geo.update")}
        </button>
      </div>

      <GeoForm
        start={draftOf(held)}
        pending={change.isPending}
        error={change.error}
        onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(back))}
        onClose={() => navigate(back)}
        onRemove={() => navigate(`/routing/geo/${held.id}/delete`)}
      />
    </div>
  )
}
