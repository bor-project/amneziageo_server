import { Link, useNavigate } from "react-router-dom"
import { useApiTokens } from "@/api/apiTokens"
import { useRoles } from "@/api/roles"
import { Rows } from "@/components/Rows"
import { titleOf } from "@/components/roles"
import { card, danger, primary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"

export function ApiTokens() {
  const t = useText()
  const language = useLanguage()
  const navigate = useNavigate()
  const tokens = useApiTokens()
  const catalog = useRoles()

  function stamp(value: string) {
    return new Date(value).toLocaleString(language)
  }

  return (
    <div className={card}>
      <div className="flex items-center justify-between gap-4 border-b border-line px-4 py-3">
        <span className="text-sm font-semibold text-ink-soft">{t("apiTokens.title")}</span>
        <Link to="/settings/users/tokens/new" className={`flex h-10 items-center ${primary}`}>
          {t("action.add")}
        </Link>
      </div>

      {tokens.data?.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("apiTokens.empty")}</div>}

      {tokens.data && tokens.data.length > 0 && (
        <Rows
          name="token"
          items={tokens.data}
          keyOf={(one) => one.id}
          columns={[
            {
              key: "name",
              caption: t("apiTokens.name"),
              sort: (one) => one.name,
              lead: true,
              body: "font-semibold text-ink",
              cell: (one) => one.name,
            },
            {
              key: "role",
              caption: t("apiTokens.role"),
              sort: (one) => titleOf(catalog.data?.roles ?? [], one.role),
              cell: (one) => titleOf(catalog.data?.roles ?? [], one.role),
            },
            {
              key: "created",
              caption: t("apiTokens.created"),
              sort: (one) => Date.parse(one.createdUtc),
              body: "whitespace-nowrap",
              cell: (one) => stamp(one.createdUtc),
            },
            {
              key: "expires",
              caption: t("apiTokens.expires"),
              sort: (one) => (one.expiresUtc === null ? Number.MAX_SAFE_INTEGER : Date.parse(one.expiresUtc)),
              body: "whitespace-nowrap",
              cell: (one) => (one.expiresUtc === null ? t("apiTokens.forever") : stamp(one.expiresUtc)),
            },
            {
              key: "used",
              caption: t("apiTokens.used"),
              sort: (one) => (one.lastUsedUtc === null ? null : Date.parse(one.lastUsedUtc)),
              body: "whitespace-nowrap",
              cell: (one) =>
                one.lastUsedUtc === null ? (
                  t("apiTokens.never")
                ) : (
                  <>
                    <div>{stamp(one.lastUsedUtc)}</div>
                    {one.lastAddress !== null && <div className="text-xs text-faint">{one.lastAddress}</div>}
                  </>
                ),
            },
            {
              key: "state",
              caption: t("apiTokens.state"),
              sort: (one) => (one.isExpired ? 1 : 0),
              cell: (one) => (
                <span className={one.isExpired ? "text-alarm" : "text-good"}>
                  {t(one.isExpired ? "apiTokens.expired" : "apiTokens.active")}
                </span>
              ),
            },
            {
              key: "actions",
              caption: t("apiTokens.actions"),
              tail: true,
              cell: (one) => (
                <div className="flex justify-end">
                  <button
                    type="button"
                    onClick={() => navigate(`/settings/users/tokens/${one.id}/delete`)}
                    className={`text-sm ${danger}`}
                  >
                    {t("apiTokens.revoke")}
                  </button>
                </div>
              ),
            },
          ]}
        />
      )}
    </div>
  )
}
