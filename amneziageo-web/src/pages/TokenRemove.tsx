import { Link, useNavigate, useParams } from "react-router-dom"
import { useApiTokens, useRevokeApiToken } from "@/api/apiTokens"
import { complaint } from "@/api/auth"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"

export function TokenRemove() {
  const t = useText()
  const language = useLanguage()
  const navigate = useNavigate()
  const { tokenId } = useParams()
  const tokens = useApiTokens()
  const revoke = useRevokeApiToken()
  const held = (tokens.data ?? []).find((one) => String(one.id) === tokenId)

  useTail(held === undefined ? [] : [{ label: held.name, to: "/settings/users" }, { label: t("apiTokens.revoke") }])

  if (held === undefined) {
    return tokens.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("apiTokens.loading")}</div>
    ) : (
      <Link to="/settings/users" className="mt-4 block text-sm text-brand-ink">
        {t("action.backToList")}
      </Link>
    )
  }

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("apiTokens.revokeTitle", { name: held.name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        <div className="mt-1 text-[13px] text-muted">
          {`${t("apiTokens.created")}: ${new Date(held.createdUtc).toLocaleString(language)}`}
        </div>
      </div>

      {revoke.error !== null && <div className="text-sm text-alarm">{t(complaint(revoke.error))}</div>}

      <div className="flex justify-end gap-2">
        <Link to="/settings/users" className={`flex h-10 items-center ${secondary}`}>
          {t("action.backToList")}
        </Link>
        <button
          type="button"
          onClick={() => void revoke.mutateAsync(held.id).then(() => navigate("/settings/users"))}
          disabled={revoke.isPending}
          className={danger}
        >
          {t("apiTokens.revoke")}
        </button>
      </div>
    </div>
  )
}
