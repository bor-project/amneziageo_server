import { Link } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useProxies } from "@/api/proxies"
import { scopes } from "@/api/scopes"
import { AppSign } from "@/components/AppMark"
import { Rows } from "@/components/Rows"
import { card } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Proxies() {
  const t = useText()
  const proxies = useProxies()
  const user = useAppSelector((s) => s.auth.user)
  const may = holds(user, scopes.manageProxies)
  return <div className={`mt-4 ${card}`}>
    {proxies.error !== null && <div className="p-4 text-sm text-alarm">{t(complaint(proxies.error))}</div>}
    {proxies.isPending && <div className="p-4 text-sm text-muted">{t("geo.loading")}</div>}
    {proxies.data?.length === 0 && <div className="p-4 text-sm text-muted">{t("proxies.empty")}</div>}
    {proxies.data !== undefined && proxies.data.length > 0 && <Rows name="proxies" items={proxies.data} keyOf={(one) => one.id} columns={[
      { key: "name", caption: t("proxies.name"), lead: true, sort: (one) => one.name, cell: (one) => may ? <Link className="font-semibold hover:text-brand-ink" to={`/routing/proxies/${one.id}/edit`}>{one.name}</Link> : one.name },
      { key: "transport", caption: t("proxies.transport"), cell: (one) => <span className="inline-flex items-center gap-1">{one.transport === "quic" ? "QUIC" : one.transport === "http" || one.transport === "xhttp" && one.protocolVersion === 2 ? `HTTP ${one.httpMethod}` : one.transport === "xhttp" ? "XHTTP" : "WebSocket"}{(one.transport === "http" || one.transport === "xhttp" && one.protocolVersion === 2) && one.httpMethod === "GET" && <AppSign className="size-3.5" />}</span> },
      { key: "address", caption: t("proxies.host"), cell: (one) => `${one.host.includes(":") ? `[${one.host}]` : one.host}:${one.port}/${one.path}` },
      { key: "enabled", caption: t("geo.enabled"), cell: (one) => one.fault ? <span className="text-alarm">{one.fault}</span> : t(one.isEnabled ? "proxies.on" : "proxies.off") },
    ]} />}
  </div>
}
