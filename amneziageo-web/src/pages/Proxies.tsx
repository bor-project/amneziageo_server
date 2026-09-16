import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { useProxies, useProxyCertificate, useSwitchProxy } from "@/api/proxies"
import type { Proxy } from "@/api/proxies"
import { scopes } from "@/api/scopes"
import { ProxyState } from "@/components/ProxyState"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { proxyFault, proxyKind, proxyPoint } from "@/components/proxy"
import { card, fieldBox, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Proxies() {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const [params, setParams] = useSearchParams()
  const proxies = useProxies()
  const tls = useProxyCertificate()
  const turn = useSwitchProxy()
  const may = holds(user, scopes.manageRouting)
  const find = params.get("find") ?? ""
  const all = proxies.data ?? []
  const shown = all.filter((one) => matches(t, one, find))

  function put(key: string, value: string) {
    const kept = new URLSearchParams(params)

    if (value === "") {
      kept.delete(key)
    } else {
      kept.set(key, value)
    }

    setParams(kept, { replace: true })
  }

  return (
    <div className={`mt-4 ${card}`}>
      {all.length === 0 && (
        <div className="flex flex-col items-center gap-4 px-6 py-10 text-center">
          <div className="text-[15px] font-medium text-ink">{t("proxies.empty")}</div>
          {may && (
            <Link to="/connections/proxies/new" className={`flex h-10 items-center ${secondary}`}>
              {t("proxies.add")}
            </Link>
          )}
        </div>
      )}

      {all.length > 0 && (
        <Rows
          name="proxy"
          items={shown}
          keyOf={(one) => one.id}
          tools={
            <input
              value={find}
              placeholder={t("action.search")}
              onChange={(e) => put("find", e.target.value)}
              className={`w-full wide:w-80 ${fieldBox}`}
            />
          }
          columns={[
            {
              key: "name",
              caption: t("proxies.name"),
              sort: (one) => one.name,
              lead: true,
              body: "font-semibold text-ink",
              cell: (one) => (
                <>
                  <Link to={`/connections/proxies/${one.id}`} className="hover:text-brand-ink">
                    {one.name}
                  </Link>
                  {!one.isEnabled && <span className="ml-2 text-xs text-muted">{t("proxies.off")}</span>}
                </>
              ),
            },
            {
              key: "kind",
              caption: t("proxies.kind"),
              sort: (one) => proxyKind(t, one),
              cell: (one) => proxyKind(t, one),
            },
            {
              key: "port",
              caption: t("proxies.port"),
              sort: (one) => one.port,
              cell: (one) => one.port,
            },
            {
              key: "address",
              caption: t("proxies.address"),
              sort: (one) => proxyPoint(one),
              cell: (one) => proxyPoint(one),
            },
            {
              key: "state",
              caption: t("proxies.state"),
              sort: (one) => (proxyFault(t, one, tls.data).length > 0 ? 2 : one.isRunning ? 0 : 1),
              cell: (one) => <ProxyState one={one} fault={proxyFault(t, one, tls.data)} />,
            },
            {
              key: "actions",
              caption: t("proxies.actions"),
              tail: true,
              cell: (one) =>
                may && (
                  <RowActions
                    title={t("proxies.actions")}
                    actions={[
                      {
                        label: one.isEnabled ? t("proxies.turnOff") : t("proxies.turnOn"),
                        onPick: () => void turn.mutateAsync({ id: one.id, on: !one.isEnabled }),
                      },
                      { label: t("proxies.edit"), onPick: () => navigate(`/connections/proxies/${one.id}/edit`) },
                      {
                        label: t("proxies.remove"),
                        onPick: () => navigate(`/connections/proxies/${one.id}/delete`),
                        alarming: true,
                      },
                    ]}
                  />
                ),
            },
          ]}
        />
      )}
    </div>
  )
}

function matches(t: Text, one: Proxy, find: string): boolean {
  const query = find.trim().toLowerCase()

  return (
    query === "" ||
    one.name.toLowerCase().includes(query) ||
    proxyKind(t, one).toLowerCase().includes(query) ||
    proxyPoint(one).toLowerCase().includes(query) ||
    String(one.port).includes(query)
  )
}
