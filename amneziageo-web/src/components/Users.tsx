import { Link, useNavigate } from "react-router-dom"
import { useRoles } from "@/api/roles"
import { useUsers } from "@/api/users"
import type { User } from "@/api/users"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, primary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

export function Users() {
  const t = useText()
  const navigate = useNavigate()
  const users = useUsers(true)
  const catalog = useRoles()

  function role(user: User): string {
    return (
      catalog.data?.roles.find((one) => one.name === user.role)?.title ??
      (user.role.length > 0 ? user.role : t("role.none"))
    )
  }

  return (
    <div className={card}>
      <div className="flex items-center justify-between gap-4 border-b border-line px-4 py-3">
        <span className="text-sm font-semibold text-ink-soft">{t("users.title")}</span>
        <Link to="/settings/users/new" className={`flex h-10 items-center ${primary}`}>
          {t("action.add")}
        </Link>
      </div>

      {users.data?.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("users.empty")}</div>}

      {users.data && users.data.length > 0 && (
        <Rows
          name="user"
          items={users.data}
          keyOf={(one) => one.name}
          columns={[
            {
              key: "name",
              caption: t("users.name"),
              sort: (one) => one.name,
              lead: true,
              cell: (one) => (
                <>
                  <Link to={`/settings/users/${one.name}/edit`} className="font-semibold text-ink hover:text-brand-ink">
                    {one.name}
                  </Link>
                  {one.displayName !== one.name && <div className="text-xs text-faint">{one.displayName}</div>}
                </>
              ),
            },
            {
              key: "kind",
              caption: t("users.kind"),
              sort: (one) => t(`users.kind.${one.kind}` as TextKey),
              cell: (one) => t(`users.kind.${one.kind}` as TextKey),
            },
            {
              key: "role",
              caption: t("users.role"),
              sort: (one) => role(one),
              cell: (one) => role(one),
            },
            {
              key: "state",
              caption: t("users.state"),
              sort: (one) => (one.enabled ? 0 : 1),
              cell: (one) => (
                <span className={one.enabled ? "text-good" : "text-alarm"}>
                  {t(one.enabled ? "users.enabled" : "users.disabled")}
                </span>
              ),
            },
            {
              key: "actions",
              caption: t("users.actions"),
              tail: true,
              cell: (one) => (
                <RowActions
                  title={t("users.actions")}
                  actions={[
                    { label: t("users.edit"), onPick: () => navigate(`/settings/users/${one.name}/edit`) },
                    { label: t("users.password"), onPick: () => navigate(`/settings/users/${one.name}/password`) },
                    {
                      label: t("users.remove"),
                      onPick: () => navigate(`/settings/users/${one.name}/delete`),
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
  )
}
