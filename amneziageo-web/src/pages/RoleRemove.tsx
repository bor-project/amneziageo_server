import { Link, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useRemoveRole, useRoles } from "@/api/roles"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function RoleRemove() {
  const t = useText()
  const navigate = useNavigate()
  const { name = "" } = useParams()
  const catalog = useRoles()
  const remove = useRemoveRole()
  const held = catalog.data?.roles.find((one) => one.name === name)

  useTail([{ label: name, to: `/settings/users/roles/${name}/edit` }, { label: t("roles.remove") }])

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("roles.removeTitle", { name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        {held !== undefined && (
          <div className="mt-1 text-[13px] text-muted">{`${t("roles.users")}: ${held.users}`}</div>
        )}
      </div>

      {remove.error !== null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}

      <div className="flex justify-end gap-2">
        <Link to="/settings/users" className={`flex h-10 items-center ${secondary}`}>
          {t("action.backToList")}
        </Link>
        <button
          type="button"
          onClick={() => void remove.mutateAsync(name).then(() => navigate("/settings/users"))}
          disabled={remove.isPending}
          className={danger}
        >
          {t("roles.remove")}
        </button>
      </div>
    </div>
  )
}
