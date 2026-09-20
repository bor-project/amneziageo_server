import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { useInterfaceTemplates } from "@/api/interfaceTemplates"
import type { InterfaceTemplate } from "@/api/interfaceTemplates"
import { scopes } from "@/api/scopes"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, fieldBox } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function InterfaceTemplates() {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const [params, setParams] = useSearchParams()
  const templates = useInterfaceTemplates()
  const may = holds(user, scopes.manageInterfaces)
  const find = params.get("find") ?? ""
  const all = templates.data ?? []
  const shown = all.filter((one) => matches(one, find))

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
      {all.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("templates.interfacesEmpty")}</div>}

      {all.length > 0 && (
        <Rows
          name="interface-template"
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
                <Link to={`/connections/templates/interfaces/${one.id}/edit`} className="hover:text-brand-ink">
                  {one.name}
                </Link>
              ),
            },
            {
              key: "port",
              caption: t("templates.startPort"),
              sort: (one) => one.listenPort,
              cell: (one) => one.listenPort,
            },
            {
              key: "subnet",
              caption: t("templates.startSubnet"),
              sort: (one) => one.subnet,
              cell: (one) => one.subnet,
            },
            {
              key: "mtu",
              caption: t("configs.mtu"),
              sort: (one) => one.mtu,
              cell: (one) => one.mtu,
            },
            {
              key: "configs",
              caption: t("templates.configs"),
              sort: (one) => one.configs,
              cell: (one) => one.configs,
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
                        onPick: () => navigate(`/connections/templates/interfaces/${one.id}/edit`),
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

function matches(one: InterfaceTemplate, find: string): boolean {
  const query = find.trim().toLowerCase()

  return query === "" || one.name.toLowerCase().includes(query) || one.subnet.includes(query)
}
