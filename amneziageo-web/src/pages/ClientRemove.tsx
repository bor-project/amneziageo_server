import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useClients, useRemoveClient } from "@/api/clients"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function ClientRemove() {
  const t = useText()
  const navigate = useNavigate()
  const { clientId } = useParams()
  const clients = useClients()
  const remove = useRemoveClient()
  const all = clients.data ?? []
  const held = all.find((one) => one.id === Number(clientId))

  useTail(held === undefined ? [] : [{ label: held.name }, { label: t("clients.remove") }])

  if (held === undefined) {
    return clients.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("clients.loading")}</div>
    ) : (
      <Navigate to="/connections/clients" replace />
    )
  }

  const devices = all.filter((one) => one.parentId === held.id)

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("clients.removeTitle", { name: held.name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        <div className="mt-1 text-[13px] text-muted">{held.address.join(", ")}</div>
        {devices.map((one) => (
          <div key={one.id} className="text-[13px] text-muted">
            {`${one.name}: ${one.address.join(", ")}`}
          </div>
        ))}
      </div>

      {remove.error !== null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}

      <div className="flex justify-end gap-2">
        <Link to="/connections/clients" className={`flex h-10 items-center ${secondary}`}>
          {t("action.backToList")}
        </Link>
        <button
          type="button"
          onClick={() => void remove.mutateAsync(held.id).then(() => navigate("/connections/clients"))}
          disabled={remove.isPending}
          className={danger}
        >
          {t("clients.remove")}
        </button>
      </div>
    </div>
  )
}
