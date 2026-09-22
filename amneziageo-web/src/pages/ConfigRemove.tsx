import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useClients } from "@/api/clients"
import { useConfigs, useRemoveConfig } from "@/api/configs"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function ConfigRemove() {
  const t = useText()
  const navigate = useNavigate()
  const { configId } = useParams()
  const configs = useConfigs()
  const clients = useClients()
  const remove = useRemoveConfig()
  const all = configs.data ?? []
  const held = all.find((one) => one.id === Number(configId))

  useTail(
    held === undefined
      ? []
      : [{ label: held.name, to: `/connections/interfaces/${held.id}/edit` }, { label: t("configs.remove") }],
  )

  if (held === undefined) {
    return configs.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("configs.loading")}</div>
    ) : (
      <Navigate to="/connections/interfaces" replace />
    )
  }

  const mine = (clients.data ?? []).filter((one) => one.configId === held.id)

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("configs.removeTitle", { name: held.name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        <div className="mt-1 text-[13px] text-muted">{held.address.join(", ")}</div>
        <div className="text-[13px] text-muted">{`${t("configs.count")}: ${mine.length}`}</div>
      </div>

      {remove.error !== null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}

      <div className="flex justify-end gap-2">
        <Link to="/connections/interfaces" className={`flex h-10 items-center ${secondary}`}>
          {t("action.backToList")}
        </Link>
        <button
          type="button"
          onClick={() => void remove.mutateAsync(held.id).then(() => navigate("/connections/interfaces"))}
          disabled={remove.isPending}
          className={danger}
        >
          {t("configs.remove")}
        </button>
      </div>
    </div>
  )
}
