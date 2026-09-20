import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { useProxyTemplates } from "@/api/proxyTemplates"
import type { ProxyTemplate } from "@/api/proxyTemplates"
import { scopes } from "@/api/scopes"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, fieldBox } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function ProxyTemplates() {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const [params, setParams] = useSearchParams()
  const templates = useProxyTemplates()
  const may = holds(user, scopes.manageRouting)
  const find = params.get("find") ?? ""
  const all = templates.data ?? []
  const shown = all.filter((one) => matches(t, one, find))

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
    <div className={`mt-4 ${card}`}>
      {all.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("templates.proxiesEmpty")}</div>}

      {all.length > 0 && (
        <Rows
          name="proxy-template"
          items={shown}
          keyOf={(one) => one.id}
          tools={
            <input
              value={find}
              placeholder={t("action.search")}
              onChange={(e) => put("find", e.target.value)}
              className={`w-full wide:w-80 ${fieldBox}`}
            />
          }
          columns={[
            {
              key: "name",
              caption: t("templates.name"),
              sort: (one) => one.name,
              lead: true,
              body: "font-semibold text-ink",
              cell: (one) => (
                <Link to={`/connections/templates/proxies/${one.id}/edit`} className="hover:text-brand-ink">
                  {one.name}
                </Link>
              ),
            },
            {
              key: "kind",
              caption: t("proxies.kind"),
              sort: (one) => kindOf(t, one),
              cell: (one) => kindOf(t, one),
            },
            {
              key: "port",
              caption: t("templates.startPort"),
              sort: (one) => one.port,
              cell: (one) => one.port,
            },
            {
              key: "proxies",
              caption: t("templates.proxies"),
              sort: (one) => one.proxies,
              cell: (one) => one.proxies,
            },
            {
              key: "actions",
              caption: t("templates.actions"),
              tail: true,
              cell: (one) =>
                may && (
                  <RowActions
                    title={t("templates.actions")}
                    actions={[
                      {
                        label: t("action.settings"),
                        onPick: () => navigate(`/connections/templates/proxies/${one.id}/edit`),
                      },
                    ]}
                  />
                ),
            },
          ]}
        />
      )}
    </div>
  )
}

function kindOf(t: Text, one: ProxyTemplate): string {
  return one.kind === "wg" ? t("proxies.kindWg") : t("proxies.kindWs")
}

function matches(t: Text, one: ProxyTemplate, find: string): boolean {
  const query = find.trim().toLowerCase()

  return query === "" || one.name.toLowerCase().includes(query) || kindOf(t, one).toLowerCase().includes(query)
}
