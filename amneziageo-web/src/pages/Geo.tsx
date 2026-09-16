import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { useGeoKeys, useGeoSources, useMoveGeoSource, useUpdateGeo, useUpdateGeoSource } from "@/api/geo"
import type { GeoSource } from "@/api/geo"
import { scopes } from "@/api/scopes"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { Box } from "@/components/fields"
import { card, field, secondary } from "@/components/styles"
import { bytes } from "@/format"
import { useLanguage, useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Geo() {
  const t = useText()
  const language = useLanguage()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const [params, setParams] = useSearchParams()
  const sources = useGeoSources()
  const keys = useGeoKeys()
  const move = useMoveGeoSource()
  const update = useUpdateGeoSource()
  const updateAll = useUpdateGeo()
  const may = holds(user, scopes.manageRouting)
  const find = params.get("find") ?? ""
  const all = sources.data ?? []
  const shown = all.filter((one) => matches(one, find))
  const last = all.length - 1

  function put(key: string, value: string) {
    const kept = new URLSearchParams(params)

    if (value === "") {
      kept.delete(key)
    } else {
      kept.set(key, value)
    }

    setParams(kept, { replace: true })
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <div className="grid gap-3.5 [grid-template-columns:repeat(auto-fit,minmax(210px,1fr))]">
        <Box caption={t("geo.countries")}>
          <div className="text-lg font-semibold text-ink">{keys.data?.countries.length ?? 0}</div>
        </Box>
        <Box caption={t("geo.categories")}>
          <div className="text-lg font-semibold text-ink">{keys.data?.categories.length ?? 0}</div>
        </Box>
      </div>

      <div className={card}>
        {all.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("geo.empty")}</div>}

        {all.length > 0 && (
          <Rows
            name="geo"
            items={shown}
            keyOf={(one) => one.id}
            tools={
              <div className="flex flex-wrap items-center gap-2">
                <input
                  value={find}
                  placeholder={t("action.search")}
                  onChange={(e) => put("find", e.target.value)}
                  className={`max-w-60 ${field}`}
                />
                {may && (
                  <button
                    type="button"
                    onClick={() => void updateAll.mutateAsync()}
                    disabled={updateAll.isPending}
                    className={secondary}
                  >
                    {updateAll.isPending ? t("geo.updating") : t("geo.updateAll")}
                  </button>
                )}
              </div>
            }
            columns={[
              {
                key: "name",
                caption: t("geo.name"),
                sort: (one) => one.name,
                lead: true,
                body: "font-semibold text-ink",
                cell: (one) => (
                  <>
                    {may ? (
                      <Link to={`/routing/geo/${one.id}/edit`} className="hover:text-brand-ink">
                        {one.name}
                      </Link>
                    ) : (
                      one.name
                    )}
                    {!one.isEnabled && <span className="ml-2 text-xs text-muted">{t("geo.off")}</span>}
                  </>
                ),
              },
              {
                key: "kind",
                caption: t("geo.kind"),
                sort: (one) => (one.kind === "geoip" ? t("geo.kindIp") : t("geo.kindSite")),
                cell: (one) => (one.kind === "geoip" ? t("geo.kindIp") : t("geo.kindSite")),
              },
              {
                key: "url",
                caption: t("geo.url"),
                sort: (one) => one.url,
                body: "max-w-72",
                cell: (one) => (
                  <span className="block truncate" title={one.url}>
                    {one.url}
                  </span>
                ),
              },
              {
                key: "entries",
                caption: t("geo.entries"),
                sort: (one) => (one.entryCount > 0 ? one.entryCount : null),
                cell: (one) => (one.entryCount > 0 ? one.entryCount : ""),
              },
              {
                key: "size",
                caption: t("geo.size"),
                sort: (one) => (one.size > 0 ? one.size : null),
                cell: (one) => (one.size > 0 ? bytes(t, one.size) : ""),
              },
              {
                key: "updated",
                caption: t("geo.updated"),
                sort: (one) => (one.updatedUtc === null ? null : Date.parse(one.updatedUtc)),
                cell: (one) => (
                  <>
                    {stamp(t, language, one)}
                    {one.lastError.length > 0 && (
                      <div className="max-w-72 truncate text-xs text-alarm" title={one.lastError}>
                        {one.lastError}
                      </div>
                    )}
                  </>
                ),
              },
              {
                key: "actions",
                caption: t("geo.actions"),
                tail: true,
                cell: (one, at) =>
                  may && (
                    <RowActions
                      title={t("geo.actions")}
                      actions={[
                        { label: t("geo.update"), onPick: () => void update.mutateAsync(one.id) },
                        { label: t("geo.edit"), onPick: () => navigate(`/routing/geo/${one.id}/edit`) },
                        ...(at > 0
                          ? [{ label: t("geo.up"), onPick: () => void move.mutateAsync({ id: one.id, up: true }) }]
                          : []),
                        ...(at < last
                          ? [{ label: t("geo.down"), onPick: () => void move.mutateAsync({ id: one.id, up: false }) }]
                          : []),
                        {
                          label: t("geo.remove"),
                          onPick: () => navigate(`/routing/geo/${one.id}/delete`),
                          alarming: true,
                        },
                      ]}
                    />
                  ),
              },
            ]}
          />
        )}
      </div>
    </div>
  )
}

function stamp(t: Text, language: string, source: GeoSource): string {
  return source.updatedUtc === null ? t("geo.never") : new Date(source.updatedUtc).toLocaleString(language)
}

function matches(one: GeoSource, find: string): boolean {
  const query = find.trim().toLowerCase()

  return query === "" || one.name.toLowerCase().includes(query) || one.url.toLowerCase().includes(query)
}
