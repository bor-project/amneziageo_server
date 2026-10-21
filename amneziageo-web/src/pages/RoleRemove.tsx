import { Link, useNavigate, useParams } from "react-router-dom"
import { useApiTokens } from "@/api/apiTokens"
import { complaint } from "@/api/auth"
import { useRemoveRole, useRoles } from "@/api/roles"
import { useUsers } from "@/api/users"
import { useTail } from "@/components/crumbs"
import { LinkLine } from "@/components/fields"
import { tokenItems, userItems } from "@/components/members"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function RoleRemove() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/settings/users")
  const { name = "" } = useParams()
  const catalog = useRoles()
  const remove = useRemoveRole()
  const team = (useUsers(true).data ?? []).filter((one) => one.role === name)
  const mine = (useApiTokens().data ?? []).filter((one) => one.role === name)

  useTail([{ label: name, to: `/settings/users/roles/${name}/edit` }, { label: t("roles.remove") }])

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("roles.removeTitle", { name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        <div className="mt-1 text-[13px] text-muted">
          {`${t("roles.users")}: `}
          {team.length === 0 ? (
            t("clients.dash")
          ) : (
            <LinkLine items={userItems(team, catalog.data?.roles ?? [], t("role.none"))} />
          )}
        </div>
        <div className="text-[13px] text-muted">
          {`${t("roles.tokens")}: `}
          {mine.length === 0 ? t("clients.dash") : <LinkLine items={tokenItems(mine)} />}
        </div>
      </div>

      {remove.error !== null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}

      <div className="flex justify-end gap-2">
        <Link to={back} className={`flex h-10 items-center ${secondary}`}>
          {t("action.backToList")}
        </Link>
        <button
          type="button"
          onClick={() => void remove.mutateAsync(name).then(() => navigate(back))}
          disabled={remove.isPending}
          className={danger}
        >
          {t("roles.remove")}
        </button>
      </div>
    </div>
  )
}
