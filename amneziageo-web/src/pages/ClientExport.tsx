import { Navigate, useParams } from "react-router-dom"
import { useClients } from "@/api/clients"
import { scopes } from "@/api/scopes"
import { ClientConfig } from "@/components/ClientConfig"
import { ClientTabs } from "@/components/ClientTabs"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"
import { lastSpot } from "@/store/spots"

export function ClientExport() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const { clientId } = useParams()
  const clients = useClients()
  const held = (clients.data ?? []).find((one) => one.id === Number(clientId))

  useTail(held === undefined ? [] : [{ label: held.name }, { label: t("action.export") }])

  if (held === undefined) {
    return clients.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("clients.loading")}</div>
    ) : (
      <Navigate to={lastSpot("connections", "/connections/clients")} replace />
    )
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <ClientTabs id={held.id} may={holds(user, scopes.manageClients) && held.parentId === null} />
      <ClientConfig id={held.id} />
    </div>
  )
}
