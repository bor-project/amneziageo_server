import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useFailure } from "@/api/failure"
import { usePresets } from "@/api/presets"
import type { Preset } from "@/api/presets"
import { scopes } from "@/api/scopes"
import { RowActions } from "@/components/RowActions"
import type { RowAction } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { Find } from "@/components/fields"
import { alarmBar, alarmLine, card } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Presets() {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const [params, setParams] = useSearchParams()
  const presets = usePresets()
  const failed = useFailure(presets)
  const may = holds(user, scopes.manageClients)
  const find = params.get("find") ?? ""
  const all = presets.data ?? []
  const list = all.filter((one) => matches(one, find))

  function put(key: string, value: string) {
    const kept = new URLSearchParams(params)

    if (value === "") {
      kept.delete(key)
    } else {
      kept.set(key, value)
    }

    setParams(kept, { replace: true })
  }

  function actions(one: Preset): RowAction[] {
    return [{ label: t("action.settings"), onPick: () => navigate(`/clients/presets/${one.id}/edit`) }]
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <div className="text-sm text-muted">{t("presets.about")}</div>

      <div className={card}>
        {presets.isPending && failed === null && (
          <div className="px-4 py-6 text-sm text-muted">{t("presets.loading")}</div>
        )}
        {failed !== null && (
          <div className={all.length === 0 ? alarmLine : alarmBar}>{t(complaint(failed))}</div>
        )}
        {presets.isSuccess && all.length === 0 && (
          <div className="px-4 py-6 text-sm text-muted">{t("presets.empty")}</div>
        )}

        {all.length > 0 && (
          <Rows
            items={list}
            keyOf={(one) => one.id}
            tools={
              <Find value={find} onChange={(value) => put("find", value)} className="w-full wide:w-72" />
            }
            columns={[
              {
                key: "name",
                width: 200,
                caption: t("presets.name"),
                sort: (one) => one.name,
                lead: true,
                body: "font-semibold text-ink",
                cell: (one) =>
                  may ? (
                    <Link to={`/clients/presets/${one.id}/edit`} className="hover:text-brand-ink">
                      {one.name}
                    </Link>
                  ) : (
                    one.name
                  ),
              },
              {
                key: "proxy",
                wrap: true,
                caption: t("presets.proxy"),
                sort: (one) => one.proxy.length,
                cell: (one) => <Short t={t} values={one.proxy} />,
              },
              {
                key: "direct",
                wrap: true,
                caption: t("presets.direct"),
                sort: (one) => one.direct.length,
                cell: (one) => <Short t={t} values={one.direct} />,
              },
              {
                key: "block",
                wrap: true,
                caption: t("presets.block"),
                sort: (one) => one.block.length,
                cell: (one) => <Short t={t} values={one.block} />,
              },
              {
                key: "templates",
                width: 120,
                caption: t("presets.templates"),
                sort: (one) => one.templates,
                cell: (one) => one.templates,
              },
              {
                key: "actions",
                width: 56,
                caption: t("presets.actions"),
                tail: true,
                cell: (one) => may && <RowActions title={t("presets.actions")} actions={actions(one)} />,
              },
            ]}
          />
        )}
      </div>
    </div>
  )
}

function Short({ t, values }: { t: Text; values: string[] }) {
  if (values.length === 0) {
    return t("clients.dash")
  }

  const head = values.slice(0, 3).join(", ")

  return (
    <span className="block truncate" title={values.join(", ")}>
      {values.length > 3 ? `${head} ${t("templates.more", { count: String(values.length - 3) })}` : head}
    </span>
  )
}

function matches(one: Preset, find: string): boolean {
  const query = find.trim().toLowerCase()

  return (
    query === "" ||
    one.name.toLowerCase().includes(query) ||
    [...one.proxy, ...one.direct, ...one.block].some((entry) => entry.toLowerCase().includes(query))
  )
}
