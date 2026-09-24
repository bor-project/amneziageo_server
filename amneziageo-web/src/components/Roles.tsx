import { Link, useNavigate } from "react-router-dom"
import { useRoles } from "@/api/roles"
import type { Role } from "@/api/roles"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, primary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"

export function Roles() {
  const t = useText()
  const navigate = useNavigate()
  const catalog = useRoles()

  return (
    <div className={card}>
      <div className="flex items-center justify-between gap-4 border-b border-line px-4 py-3">
        <span className="text-sm font-semibold text-ink-soft">{t("roles.title")}</span>
        <Link to="/settings/users/roles/new" className={`flex h-10 items-center ${primary}`}>
          {t("action.add")}
        </Link>
      </div>

      {catalog.data?.roles.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("roles.empty")}</div>}

      {catalog.data && catalog.data.roles.length > 0 && (
        <Rows
          name="role"
          items={catalog.data.roles}
          keyOf={(one) => one.name}
          columns={[
            {
              key: "title",
              width: 176,
              caption: t("roles.label"),
              sort: (one) => (one.title.length > 0 ? one.title : one.name),
              lead: true,
              cell: (one) => (
                <>
                  <Link
                    to={`/settings/users/roles/${one.name}/edit`}
                    className="font-semibold text-ink hover:text-brand-ink"
                  >
                    {one.title.length > 0 ? one.title : one.name}
                  </Link>
                  <div className="text-xs text-faint">
                    {one.name}
                    {one.builtin ? ` · ${t("roles.builtin")}` : ""}
                  </div>
                </>
              ),
            },
            {
              key: "rights",
              wrap: true,
              caption: t("roles.rights"),
              sort: (one) => rights(t, one),
              cell: (one) => rights(t, one),
            },
            {
              key: "users",
              width: 160,
              caption: t("roles.users"),
              sort: (one) => one.users,
              cell: (one) => one.users,
            },
            {
              key: "actions",
              width: 56,
              caption: t("roles.actions"),
              tail: true,
              cell: (one) => (
                <RowActions
                  title={t("roles.actions")}
                  actions={[
                    { label: t("action.settings"), onPick: () => navigate(`/settings/users/roles/${one.name}/edit`) },
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

function rights(t: Text, role: Role): string {
  return role.scopes.length === 0
    ? t("roles.none")
    : role.scopes.map((scope) => t(`scope.${scope}` as TextKey)).join(", ")
}
