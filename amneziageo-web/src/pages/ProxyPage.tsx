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
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { proxyTrail } from "@/pages/trails"

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
  const fresh = useFreshProxy(proxies.data !== undefined, nextName(proxies.data))
  const add = useAddProxy()

  useTail([{ label: t("proxies.newTitle") }])

  if (fresh.data === undefined) {
    return <div className="mt-4 text-sm text-muted">{t("proxies.loading")}</div>
  }

  return (
    <ProxyForm
      start={draftOf(fresh.data)}
      panel={tls.data}
      addresses={local.data ?? []}
      pending={add.isPending}
      error={add.error}
      onSave={(draft) => void add.mutateAsync(draft).then((made) => navigate(`/connections/proxies/${made.id}`))}
      onClose={() => navigate("/connections/proxies")}
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

  useTail(held === undefined ? [] : [proxyTrail(held, all), { label: t("proxies.edit") }])

  if (held === undefined) {
    return proxies.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("proxies.loading")}</div>
    ) : (
      <Navigate to="/connections/proxies" replace />
    )
  }

  const card = `/connections/proxies/${held.id}`

  return (
    <ProxyForm
      start={draftOf(held)}
      panel={tls.data}
      addresses={local.data ?? []}
      pending={change.isPending}
      error={change.error}
      onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(card))}
      onClose={() => navigate(card)}
    />
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
