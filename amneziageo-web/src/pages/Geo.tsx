import { useState } from "react"
import {
  draftOf,
  useAddGeoSource,
  useChangeGeoSource,
  useGeoKeys,
  useGeoSources,
  useMoveGeoSource,
  useRemoveGeoSource,
  useUpdateGeo,
  useUpdateGeoSource,
} from "@/api/geo"
import type { GeoSource } from "@/api/geo"
import { scopes } from "@/api/scopes"
import { GeoForm } from "@/components/GeoForm"
import { Modal } from "@/components/Modal"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, danger, primary, secondary } from "@/components/styles"
import { bytes } from "@/format"
import { useLanguage, useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

const fresh = { name: "", kind: "geoip" as const, url: "", isEnabled: true }

export function Geo() {
  const t = useText()
  const language = useLanguage()
  const user = useAppSelector((s) => s.auth.user)
  const sources = useGeoSources()
  const keys = useGeoKeys()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<GeoSource | null>(null)
  const [removing, setRemoving] = useState<GeoSource | null>(null)
  const add = useAddGeoSource()
  const change = useChangeGeoSource()
  const remove = useRemoveGeoSource()
  const move = useMoveGeoSource()
  const update = useUpdateGeoSource()
  const updateAll = useUpdateGeo()
  const may = holds(user, scopes.manageRouting)
  const last = (sources.data?.length ?? 0) - 1

  return (
    <div>
      <div className={`mt-4 flex flex-wrap gap-6 px-4 py-3 ${card}`}>
        <Tally caption={t("geo.countries")} value={keys.data?.countries.length ?? 0} />
        <Tally caption={t("geo.categories")} value={keys.data?.categories.length ?? 0} />
      </div>

      <div className={`mt-4 ${card}`}>
        {may && (
          <div className="flex justify-end gap-2 border-b border-line px-4 py-3">
            <button
              type="button"
              onClick={() => void updateAll.mutateAsync()}
              disabled={updateAll.isPending}
              className={secondary}
            >
              {updateAll.isPending ? t("geo.updating") : t("geo.updateAll")}
            </button>
            <button type="button" onClick={() => setAdding(true)} className={primary}>
              {t("geo.add")}
            </button>
          </div>
        )}

        {sources.data?.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("geo.empty")}</div>}

        {sources.data && sources.data.length > 0 && (
          <Rows
            items={sources.data}
            keyOf={(source) => source.id}
            columns={[
              {
                key: "name",
                caption: t("geo.name"),
                lead: true,
                body: "font-medium text-ink",
                cell: (source) => (
                  <>
                    {source.name}
                    {!source.isEnabled && <span className="ml-2 text-xs text-muted">{t("geo.off")}</span>}
                  </>
                ),
              },
              {
                key: "kind",
                caption: t("geo.kind"),
                body: "text-muted",
                cell: (source) => (source.kind === "geoip" ? t("geo.kindIp") : t("geo.kindSite")),
              },
              {
                key: "url",
                caption: t("geo.url"),
                body: "max-w-72 text-muted",
                cell: (source) => (
                  <span className="block truncate" title={source.url}>
                    {source.url}
                  </span>
                ),
              },
              {
                key: "entries",
                caption: t("geo.entries"),
                body: "text-muted",
                cell: (source) => (source.entryCount > 0 ? source.entryCount : ""),
              },
              {
                key: "size",
                caption: t("geo.size"),
                body: "text-muted",
                cell: (source) => (source.size > 0 ? bytes(t, source.size) : ""),
              },
              {
                key: "updated",
                caption: t("geo.updated"),
                body: "text-muted",
                cell: (source) => (
                  <>
                    {stamp(t, language, source)}
                    {source.lastError.length > 0 && (
                      <div className="max-w-72 truncate text-xs text-alarm" title={source.lastError}>
                        {source.lastError}
                      </div>
                    )}
                  </>
                ),
              },
              {
                key: "actions",
                caption: t("geo.actions"),
                tail: true,
                cell: (source, at) =>
                  may && (
                    <RowActions
                      title={t("geo.actions")}
                      actions={[
                        { label: t("geo.update"), onPick: () => void update.mutateAsync(source.id) },
                        { label: t("geo.edit"), onPick: () => setEditing(source) },
                        ...(at > 0
                          ? [{ label: t("geo.up"), onPick: () => void move.mutateAsync({ id: source.id, up: true }) }]
                          : []),
                        ...(at < last
                          ? [
                              {
                                label: t("geo.down"),
                                onPick: () => void move.mutateAsync({ id: source.id, up: false }),
                              },
                            ]
                          : []),
                        { label: t("geo.remove"), onPick: () => setRemoving(source), alarming: true },
                      ]}
                    />
                  ),
              },
            ]}
          />
        )}
      </div>

      {adding && (
        <GeoForm
          title={t("geo.newTitle")}
          start={fresh}
          pending={add.isPending}
          error={add.error}
          onSave={(draft) => void add.mutateAsync(draft).then(() => setAdding(false))}
          onClose={() => setAdding(false)}
        />
      )}

      {editing && (
        <GeoForm
          title={t("geo.editTitle", { name: editing.name })}
          start={draftOf(editing)}
          pending={change.isPending}
          error={change.error}
          onSave={(draft) =>
            void change.mutateAsync({ id: editing.id, draft }).then(() => setEditing(null))
          }
          onClose={() => setEditing(null)}
        />
      )}

      {removing && (
        <Modal
          title={t("geo.removeTitle", { name: removing.name })}
          onClose={() => setRemoving(null)}
          footer={
            <>
              <button type="button" onClick={() => setRemoving(null)} className={secondary}>
                {t("geo.cancel")}
              </button>
              <button
                type="button"
                onClick={() => void remove.mutateAsync(removing.id).then(() => setRemoving(null))}
                disabled={remove.isPending}
                className={danger}
              >
                {t("geo.remove")}
              </button>
            </>
          }
        >
          <div className="text-sm text-muted">{removing.url}</div>
        </Modal>
      )}
    </div>
  )
}

function Tally({ caption, value }: { caption: string; value: number }) {
  return (
    <div>
      <div className="text-xs text-muted">{caption}</div>
      <div className="text-lg font-semibold text-ink">{value}</div>
    </div>
  )
}

function stamp(t: Text, language: string, source: GeoSource): string {
  return source.updatedUtc === null ? t("geo.never") : new Date(source.updatedUtc).toLocaleString(language)
}
