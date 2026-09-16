import { useState } from "react"
import {
  draftOf,
  useAddProxy,
  useChangeProxy,
  useFreshProxy,
  useProxies,
  useProxyAddresses,
  useProxyCertificate,
  useRemoveProxy,
  useSwitchProxy,
  uncertified,
} from "@/api/proxies"
import type { Proxy } from "@/api/proxies"
import { scopes } from "@/api/scopes"
import { ProxyForm } from "@/components/ProxyForm"
import { Modal } from "@/components/Modal"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, danger, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Proxies() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const proxies = useProxies()
  const tls = useProxyCertificate()
  const local = useProxyAddresses()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<Proxy | null>(null)
  const [removing, setRemoving] = useState<Proxy | null>(null)
  const fresh = useFreshProxy(adding, nextName(proxies.data))
  const add = useAddProxy()
  const change = useChangeProxy()
  const remove = useRemoveProxy()
  const turn = useSwitchProxy()
  const may = holds(user, scopes.manageRouting)

  return (
    <div>
      <div className={`mt-4 ${card}`}>
        {may && (
          <div className="flex justify-end gap-2 border-b border-line px-4 py-3">
            <button type="button" onClick={() => setAdding(true)} className={primary}>
              {t("proxies.add")}
            </button>
          </div>
        )}

        {proxies.data?.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("proxies.empty")}</div>}

        {proxies.data && proxies.data.length > 0 && (
          <Rows
            items={proxies.data}
            keyOf={(proxy) => proxy.id}
            columns={[
              {
                key: "name",
                caption: t("proxies.name"),
                sort: (proxy) => proxy.name,
                lead: true,
                body: "font-medium text-ink",
                cell: (proxy) => (
                  <>
                    {proxy.name}
                    {!proxy.isEnabled && <span className="ml-2 text-xs text-muted">{t("proxies.off")}</span>}
                  </>
                ),
              },
              {
                key: "kind",
                caption: t("proxies.kind"),
                sort: (proxy) => (proxy.kind === "wg" ? t("proxies.kindWg") : t("proxies.kindWs")),
                body: "text-muted",
                cell: (proxy) => (proxy.kind === "wg" ? t("proxies.kindWg") : t("proxies.kindWs")),
              },
              {
                key: "port",
                caption: t("proxies.port"),
                sort: (proxy) => proxy.port,
                body: "text-muted",
                cell: (proxy) => proxy.port,
              },
              {
                key: "address",
                caption: t("proxies.address"),
                sort: (proxy) => (proxy.kind === "wg" ? proxy.target : `/${proxy.path}`),
                body: "text-muted",
                cell: (proxy) => (proxy.kind === "wg" ? proxy.target : `/${proxy.path}`),
              },
              {
                key: "state",
                caption: t("proxies.state"),
                sort: (proxy) => (proxy.message.length > 0 || uncertified(tls.data, proxy) ? 2 : proxy.isRunning ? 0 : 1),
                cell: (proxy) => (
                  <State
                    proxy={proxy}
                    fault={proxy.message || (uncertified(tls.data, proxy) ? t("error.noCertificate") : "")}
                    running={t("proxies.running")}
                    stopped={t("proxies.stopped")}
                  />
                ),
              },
              {
                key: "actions",
                caption: t("proxies.actions"),
                tail: true,
                cell: (proxy) =>
                  may && (
                    <RowActions
                      title={t("proxies.actions")}
                      actions={[
                        {
                          label: proxy.isEnabled ? t("proxies.turnOff") : t("proxies.turnOn"),
                          onPick: () => void turn.mutateAsync({ id: proxy.id, on: !proxy.isEnabled }),
                        },
                        { label: t("proxies.edit"), onPick: () => setEditing(proxy) },
                        { label: t("proxies.remove"), onPick: () => setRemoving(proxy), alarming: true },
                      ]}
                    />
                  ),
              },
            ]}
          />
        )}
      </div>

      {adding && fresh.data && (
        <ProxyForm
          title={t("proxies.newTitle")}
          start={draftOf(fresh.data)}
          panel={tls.data}
          addresses={local.data ?? []}
          pending={add.isPending}
          error={add.error}
          onSave={(draft) => void add.mutateAsync(draft).then(() => setAdding(false))}
          onClose={() => setAdding(false)}
        />
      )}

      {editing && (
        <ProxyForm
          title={t("proxies.editTitle", { name: editing.name })}
          start={draftOf(editing)}
          panel={tls.data}
          addresses={local.data ?? []}
          pending={change.isPending}
          error={change.error}
          onSave={(draft) => void change.mutateAsync({ id: editing.id, draft }).then(() => setEditing(null))}
          onClose={() => setEditing(null)}
        />
      )}

      {removing && (
        <Modal
          title={t("proxies.removeTitle", { name: removing.name })}
          onClose={() => setRemoving(null)}
          footer={
            <>
              <button type="button" onClick={() => setRemoving(null)} className={secondary}>
                {t("proxies.cancel")}
              </button>
              <button
                type="button"
                onClick={() => void remove.mutateAsync(removing.id).then(() => setRemoving(null))}
                disabled={remove.isPending}
                className={danger}
              >
                {t("proxies.remove")}
              </button>
            </>
          }
        >
          <div className="text-sm text-muted">
            {removing.port}
            {removing.kind === "wg" ? ` ${removing.target}` : ` /${removing.path}`}
          </div>
        </Modal>
      )}
    </div>
  )
}

function State({
  proxy,
  fault,
  running,
  stopped,
}: {
  proxy: Proxy
  fault: string
  running: string
  stopped: string
}) {
  if (fault.length > 0) {
    return (
      <span className="text-alarm" title={fault}>
        {stopped}
      </span>
    )
  }

  return <span className={proxy.isRunning ? "text-ink" : "text-muted"}>{proxy.isRunning ? running : stopped}</span>
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
