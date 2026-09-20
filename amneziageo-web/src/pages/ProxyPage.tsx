import { Navigate, useNavigate, useParams } from "react-router-dom"
import {
  draftOf,
  useAddProxy,
  useChangeProxy,
  useFreshProxy,
  useProxies,
  useProxyAddresses,
  useProxyCertificate,
} from "@/api/proxies"
import type { Proxy } from "@/api/proxies"
import { ProxyForm } from "@/components/ProxyForm"
import { ProxyState } from "@/components/ProxyState"
import { useTail } from "@/components/crumbs"
import { proxyFault } from "@/components/proxy"
import { chip } from "@/components/styles"
import { useText } from "@/i18n"
import { lastSpot } from "@/store/spots"

export function ProxyPage() {
  const { proxyId } = useParams()

  return proxyId === undefined ? <NewProxy /> : <HeldProxy proxyId={Number(proxyId)} />
}

function NewProxy() {
  const t = useText()
  const navigate = useNavigate()
  const proxies = useProxies()
  const tls = useProxyCertificate()
  const local = useProxyAddresses()
  const fresh = useFreshProxy(proxies.data !== undefined, nextName(proxies.data), "ws")
  const relay = useFreshProxy(proxies.data !== undefined, nextName(proxies.data), "wg")
  const add = useAddProxy()
  const back = lastSpot("connections", "/connections/proxies")

  useTail([{ label: t("proxies.newTitle") }])

  if (fresh.data === undefined || relay.data === undefined) {
    return <div className="mt-4 text-sm text-muted">{t("proxies.loading")}</div>
  }

  return (
    <ProxyForm
      start={draftOf(fresh.data)}
      fresh={{ ws: draftOf(fresh.data), wg: draftOf(relay.data) }}
      panel={tls.data}
      addresses={local.data ?? []}
      pending={add.isPending}
      error={add.error}
      onSave={(draft) => void add.mutateAsync(draft).then(() => navigate(back))}
      onClose={() => navigate(back)}
    />
  )
}

function HeldProxy({ proxyId }: { proxyId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const proxies = useProxies()
  const tls = useProxyCertificate()
  const local = useProxyAddresses()
  const change = useChangeProxy()
  const all = proxies.data ?? []
  const held = all.find((one) => one.id === proxyId)
  const back = lastSpot("connections", "/connections/proxies")

  useTail(held === undefined ? [] : [{ label: held.name }, { label: t("proxies.edit") }])

  if (held === undefined) {
    return proxies.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("proxies.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  const fault = proxyFault(t, held, tls.data)

  return (
    <div>
      <div className="mt-4 flex items-center gap-2">
        <span className={chip}>
          <ProxyState one={held} fault={fault} />
        </span>
        {fault.length > 0 && <span className="text-sm text-alarm">{fault}</span>}
      </div>

      <ProxyForm
        start={draftOf(held)}
        panel={tls.data}
        addresses={local.data ?? []}
        pending={change.isPending}
        error={change.error}
        onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(back))}
        onClose={() => navigate(back)}
        onRemove={() => navigate(`/connections/proxies/${held.id}/delete`)}
      />
    </div>
  )
}

function nextName(proxies: Proxy[] | undefined): string {
  const taken = new Set((proxies ?? []).map((one) => one.name))
  for (let at = 0; at < 100; at++) {
    const name = `proxy${at}`
    if (!taken.has(name)) {
      return name
    }
  }

  return "proxy0"
}
