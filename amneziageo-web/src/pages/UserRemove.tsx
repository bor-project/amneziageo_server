import { Link, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useRemoveUser, useUsers } from "@/api/users"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function UserRemove() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/settings/users")
  const { name = "" } = useParams()
  const users = useUsers(true)
  const remove = useRemoveUser()
  const held = (users.data ?? []).find((one) => one.name === name)

  useTail([{ label: name, to: `/settings/users/${name}/edit` }, { label: t("users.remove") }])

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("users.removeTitle", { name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        {held !== undefined && held.displayName !== held.name && (
          <div className="mt-1 text-[13px] text-muted">{held.displayName}</div>
        )}
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
          {t("users.remove")}
        </button>
      </div>
    </div>
  )
}
