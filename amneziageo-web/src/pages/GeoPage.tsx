import { Navigate, useNavigate, useParams } from "react-router-dom"
import { draftOf, useAddGeoSource, useChangeGeoSource, useGeoSources, useUpdateGeoSource } from "@/api/geo"
import type { GeoSourceDraft } from "@/api/geo"
import { GeoForm } from "@/components/GeoForm"
import { secondary } from "@/components/styles"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"

const fresh: GeoSourceDraft = { name: "", kind: "geoip", url: "", isEnabled: true }

export function GeoPage() {
  const { sourceId } = useParams()

  return sourceId === undefined ? <NewSource /> : <HeldSource sourceId={Number(sourceId)} />
}

function NewSource() {
  const t = useText()
  const navigate = useNavigate()
  const add = useAddGeoSource()

  useTail([{ label: t("geo.newTitle") }])

  return (
    <GeoForm
      start={fresh}
      pending={add.isPending}
      error={add.error}
      onSave={(draft) => void add.mutateAsync(draft).then(() => navigate("/routing/geo"))}
      onClose={() => navigate("/routing/geo")}
    />
  )
}

function HeldSource({ sourceId }: { sourceId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const sources = useGeoSources()
  const change = useChangeGeoSource()
  const update = useUpdateGeoSource()
  const all = sources.data ?? []
  const held = all.find((one) => one.id === sourceId)

  useTail(held === undefined ? [] : [{ label: held.name }, { label: t("geo.edit") }])

  if (held === undefined) {
    return sources.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("geo.loading")}</div>
    ) : (
      <Navigate to="/routing/geo" replace />
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
        onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate("/routing/geo"))}
        onClose={() => navigate("/routing/geo")}
        onRemove={() => navigate(`/routing/geo/${held.id}/delete`)}
      />
    </div>
  )
}
