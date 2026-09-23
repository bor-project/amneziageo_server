import { Link, Navigate, useParams } from "react-router-dom"
import { useClients } from "@/api/clients"
import { scopes } from "@/api/scopes"
import { ClientConfig } from "@/components/ClientConfig"
import { useTail } from "@/components/crumbs"
import { secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"
import { useSpot } from "@/store/spots"

export function ClientExport() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const { clientId } = useParams()
  const clients = useClients()
  const back = useSpot("/connections/clients")
  const held = (clients.data ?? []).find((one) => one.id === Number(clientId))

  useTail(held === undefined ? [] : [{ label: held.name }])

  if (held === undefined) {
    return clients.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("clients.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  const may = holds(user, scopes.manageClients) && held.parentId === null

  return (
    <div className="mt-4 flex flex-col gap-4">
      {may && (
        <div className="flex justify-end">
          <Link to={`/connections/clients/${held.id}/edit`} className={secondary}>
            {t("action.settings")}
          </Link>
        </div>
      )}
      <ClientConfig id={held.id} editable={may} />
    </div>
  )
}
