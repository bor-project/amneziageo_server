import { useState } from "react"
import axios from "axios"
import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { usePanel } from "@/api/panel"
import { useProxies, useProxyDraft, useRemoveProxy, useSaveProxy } from "@/api/proxies"
import type { ProxyDraft, TransportProxy } from "@/api/proxies"
import { AppSign } from "@/components/AppMark"
import { Count, Line, Part, Pick, Regenerate, Switch } from "@/components/fields"
import { pathFault, usePathHolders, useSocketHolders } from "@/components/ports"
import { KeepQuestion } from "@/components/PortNote"
import { useTail } from "@/components/crumbs"
import { danger, footer, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { same } from "@/store/draftSlice"
import { useSpot } from "@/store/spots"

export function ProxyPage() {
  const { proxyId } = useParams()
  const all = useProxies()
  const fresh = useProxyDraft(proxyId === undefined)
  const held = proxyId === undefined ? fresh.data : all.data?.find((one) => one.id === Number(proxyId))
  const t = useText()
  useTail([{ label: held?.name || t("proxies.new") }])
  if (held === undefined) {
    return all.isPending || fresh.isPending && proxyId === undefined ? <div className="mt-4 text-sm text-muted">{t("geo.loading")}</div> : <Navigate to="/routing/proxies" replace />
  }
  return <ProxyForm key={held.id} start={held} />
}

function ProxyForm({ start }: { start: TransportProxy }) {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/proxies")
  const save = useSaveProxy()
  const settings = usePanel().data
  const initial = { ...start, transport: start.transport === "xhttp" && start.protocolVersion === 2 ? "http" as const : start.transport,
    useCertificate: start.useCertificate ?? true, targets: start.targets ?? { restricted: false, destinations: [] } }
  const [draft, setDraft] = useState<ProxyDraft>(initial)
  const [asking, setAsking] = useState(false)
  const edited = !same(draft, initial)
  const targets = draft.targets!
  const targetsValid = targets.destinations.length <= 64 && (!targets.restricted || targets.destinations.length > 0) && targets.destinations.every((one) => one.host.trim().length > 0 && one.port >= 1 && one.port <= 65535)
  const paths = [...usePathHolders({ proxy: start.id, config: start.legacyConfigId ?? undefined }), ...useSocketHolders({ config: start.legacyConfigId ?? undefined })]
  const listenPort = draft.listenPort || draft.port
  const path = pathFault(t, draft.path, listenPort, paths)
  const valid = draft.useCertificate === true && !draft.originHttp && draft.transport !== "xhttp" && draft.name.trim().length > 0 && draft.host.trim().length > 0 && draft.port >= 1 && draft.port <= 65535 && draft.listenPort >= 0 && draft.listenPort <= 65535 && path.length === 0 && targetsValid
  function put(change: Partial<ProxyDraft>) { setDraft({ ...draft, ...change }) }
  function destination(index: number, change: Partial<{ host: string; port: number }>) {
    put({ targets: { ...targets, destinations: targets.destinations.map((one, at) => at === index ? { ...one, ...change } : one) } })
  }
  function send(keep?: boolean) {
    setAsking(false)
    void save.mutateAsync({ id: start.id, draft: { ...draft, keep } }).then(() => navigate(back))
  }
  function certificate(domain: string) {
    if (domain === "__own") { return }
    put(domain.length === 0 ? { certificate: "", certificateKey: "" } : {
      certificate: `${settings?.certificateRoot ?? "/etc/letsencrypt/live"}/${domain}/fullchain.pem`,
      certificateKey: `${settings?.certificateRoot ?? "/etc/letsencrypt/live"}/${domain}/privkey.pem`,
    })
  }
  const domain = draft.certificate.length === 0 ? "" : (settings?.certificates ?? []).find((name) => draft.certificate === `${settings?.certificateRoot}/${name}/fullchain.pem`) ?? "__own"
  const conflict = axios.isAxiosError(save.error) ? (save.error.response?.data as { entity?: string } | undefined)?.entity : undefined
  return <div className="mt-4 flex flex-col gap-4">
    <Part title={t("proxies.main")}>
      <div className="sm:col-span-2"><Switch id="proxy-enabled" caption={t("geo.enabled")} value={draft.isEnabled} onChange={(isEnabled) => put({ isEnabled })} /></div>
      <Line id="proxy-name" caption={t("proxies.name")} value={draft.name} onChange={(name) => put({ name })} />
      <Pick id="proxy-transport" caption={t("proxies.transport")} value={draft.transport} onChange={(transport) => put({ transport: transport as ProxyDraft["transport"],
        protocolVersion: transport === "http" ? 2 : 0, httpMethod: "GET" })}>
        <option value="websocket">WebSocket</option><option value="http">HTTP</option><option value="quic">QUIC</option>
        {draft.transport === "xhttp" && <option value="xhttp">XHTTP</option>}
      </Pick>
      <div className="sm:col-span-2"><Switch id="proxy-hosted" caption={t("proxies.hosted")} value={draft.hosted} onChange={(hosted) => put({ hosted })} /></div>
      <Line id="proxy-host" caption={t("proxies.host")} value={draft.host} onChange={(host) => put({ host: host.trim() })} />
      <Count id="proxy-port" caption={t("proxies.port")} value={draft.port} onChange={(port) => put({ port })} />
      <Count id="proxy-listen-port" caption={t("proxies.listenPort")} value={draft.listenPort} unset={String(draft.port)} onChange={(listenPort) => put({ listenPort })} />
      {draft.transport === "http" && <>
        <Pick id="proxy-http-method" caption={<span className="flex items-center gap-1">{t("proxies.httpMethod")}{draft.httpMethod === "GET" && <AppSign className="size-3.5" />}</span>} value={draft.httpMethod} onChange={(httpMethod) => put({ httpMethod: httpMethod as "GET" | "POST" })}>
          <option value="GET">GET</option><option value="POST">POST</option>
        </Pick>
      </>}
      <Line id="proxy-path" caption={t("proxies.path")} value={draft.path} onChange={(value) => put({ path: value.trim().replace(/^\/+|\/+$/g, "") })} fault={path}
        after={<Regenerate title={t("proxies.generate")} onClick={() => put({ path: [...crypto.getRandomValues(new Uint8Array(8))].map((one) => one.toString(16).padStart(2, "0")).join("") })} />} />
    </Part>
    <Part title={t("proxies.access")}>
      <Switch id="proxy-opened" caption={t("proxies.openPort")} value={draft.opened} onChange={(opened) => put({ opened })} />
    </Part>
    <Part title={t("proxies.targets")}>
      <div className="sm:col-span-2"><Switch id="proxy-restricted" caption={t("proxies.restricted")} value={targets.restricted} onChange={(restricted) => put({ targets: { ...targets, restricted } })} /></div>
      {targets.destinations.map((one, index) => <div key={index} className="grid grid-cols-[1fr_8rem_auto] items-end gap-2 sm:col-span-2">
        <Line id={`proxy-target-host-${index}`} caption={t("proxies.targetHost")} value={one.host} onChange={(host) => destination(index, { host })} />
        <Count id={`proxy-target-port-${index}`} caption={t("proxies.port")} value={one.port} onChange={(port) => destination(index, { port })} />
        <button type="button" className={secondary} aria-label={t("geo.remove")} onClick={() => put({ targets: { ...targets, destinations: targets.destinations.filter((_, at) => at !== index) } })}>×</button>
      </div>)}
      <div className="sm:col-span-2"><button type="button" className={secondary} disabled={targets.destinations.length >= 64} onClick={() => put({ targets: { ...targets, destinations: [...targets.destinations, { host: "", port: 51820 }] } })}>{t("action.add")}</button></div>
    </Part>
    <Part title={t("settings.partCertificate")}>
      {(!draft.useCertificate || draft.originHttp) && <div className="sm:col-span-2"><button type="button" className={secondary} onClick={() => put({ useCertificate: true, originHttp: false })}>{t("proxies.enableTls")}</button></div>}
      <Pick id="proxy-certificate-domain" caption={t("settings.domain")} value={domain} onChange={certificate}>
        <option value="">{t("proxies.panelCertificate")}</option>
        <option value="__own">{t("settings.ownCertificate")}</option>
        {(settings?.certificates ?? []).map((domain) => <option key={domain} value={domain}>{domain}</option>)}
      </Pick><div />
      <Line id="proxy-certificate" caption={t("settings.certificate")} value={draft.certificate} onChange={(value) => put({ certificate: value })} wide />
      <Line id="proxy-certificate-key" caption={t("settings.certificateKey")} value={draft.certificateKey} onChange={(value) => put({ certificateKey: value })} wide />
      <Line id="proxy-server-name" caption={t("proxies.serverName")} value={draft.serverName} onChange={(serverName) => put({ serverName })} />
      <Line id="proxy-pin" caption={t("proxies.pin")} value={draft.pin} onChange={(pin) => put({ pin })} />
    </Part>
    {save.error !== null && <div className="text-sm text-alarm">{conflict === undefined ? t(complaint(save.error)) : t("error.pathTaken", { name: conflict })}</div>}
    <div className={footer}>
      {start.id > 0 && <Link to={`/routing/proxies/${start.id}/delete`} className={`mr-auto ${danger}`}>{t("geo.remove")}</Link>}
      <button type="button" className={secondary} disabled={!edited || save.isPending} onClick={() => navigate(back)}>{t("geo.cancel")}</button>
      <button type="button" className={primary} disabled={!edited || !valid || save.isPending} onClick={() => start.opened && (start.listenPort || start.port) !== listenPort ? setAsking(true) : send()}>{t("geo.save")}</button>
    </div>
    {asking && <KeepQuestion ports={[`${start.listenPort || start.port}/${start.transport === "quic" ? "udp" : "tcp"}`]} onAnswer={send} onCancel={() => setAsking(false)} />}
  </div>
}

export function ProxyRemove() {
  const t = useText()
  const { proxyId } = useParams()
  const all = useProxies()
  const held = all.data?.find((one) => one.id === Number(proxyId))
  const remove = useRemoveProxy()
  const navigate = useNavigate()
  const back = useSpot("/routing/proxies")
  useTail([{ label: held?.name ?? "", to: `/routing/proxies/${proxyId}/edit` }, { label: t("geo.remove") }])
  if (held === undefined) { return all.isPending ? null : <Navigate to={back} replace /> }
  return <div className="mt-4 flex flex-col gap-4">
    <h2 className="text-[22px] font-semibold">{t("proxies.remove", { name: held.name })}</h2>
    {remove.error !== null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}
    <div className="flex justify-end gap-2"><Link className={secondary} to={back}>{t("action.backToList")}</Link>
      <button type="button" className={danger} disabled={remove.isPending} onClick={() => void remove.mutateAsync(held.id).then(() => navigate(back))}>{t("geo.remove")}</button>
    </div>
  </div>
}
