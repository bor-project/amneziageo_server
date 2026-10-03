import { useState } from "react"
import { span } from "@/address"
import { complaint } from "@/api/auth"
import { downedOf, failure, useConfigs, useImportConfig, useKeyPair, usePresharedKey } from "@/api/configs"
import type { ConfigDraft, Obfuscation } from "@/api/configs"
import type { Inbound } from "@/api/clients"
import { usePortState } from "@/api/firewall"
import type { Holding } from "@/api/firewall"
import { scopes } from "@/api/scopes"
import { ObfuscationFields } from "@/components/Obfuscation"
import { KeepQuestion, PortNote } from "@/components/PortNote"
import { Count, Flag, Help, Hinted, Line, ListLine, Part, Pick, Regenerate, Switch } from "@/components/fields"
import { manual, useOpening } from "@/components/opening"
import { pathFault, portFault, usePathHolders, usePortHolders, useServicesHolders } from "@/components/ports"
import { danger, field, footer, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { same } from "@/store/draftSlice"

export function ConfigForm({
  start,
  publicKey,
  self,
  pending,
  error,
  fault = "",
  faultOf = "",
  onSave,
  onClose,
  onRemove,
  importable = false,
}: {
  start: ConfigDraft
  publicKey: string
  self?: number
  pending: boolean
  error: unknown
  fault?: string
  faultOf?: string
  onSave: (draft: ConfigDraft, holding: Holding) => void
  onClose: () => void
  onRemove?: () => void
  importable?: boolean
}) {
  const t = useText()
  const keys = useKeyPair()
  const shared = usePresharedKey()
  const configs = useConfigs().data ?? []
  const others = configs.filter((one) => one.id !== self)
  const saved = configs.find((one) => one.id === self)
  const opened = saved?.opened ?? false
  const { able, reach, ufw } = useOpening(scopes.manageInterfaces)
  const [opening, setOpening] = useState(false)
  const [asking, setAsking] = useState(false)
  const held = usePortHolders({ config: self })
  const taken = useServicesHolders()
  const claimed = usePathHolders()
  const [draft, setDraft] = useState(start)
  const [shown, setShown] = useState(publicKey)
  const read = useImportConfig()
  const [text, setText] = useState("")
  const failed = error !== null && error !== undefined
  const said = failed ? failure(t, error) : fault
  const proxy = (failed ? downedOf(error)?.error : faultOf) === "websocket-down" ? said : ""
  const port = portFault(t, draft.listenPort, held)
  const services = draft.servicesPort === 0 ? "" : portFault(t, draft.servicesPort, taken)
  const servicesAt = draft.servicesPort > 0 ? draft.servicesPort : draft.listenPort
  const servicesWere = start.servicesPort > 0 ? start.servicesPort : start.listenPort
  const udpMoved = saved === undefined || draft.listenPort !== start.listenPort
  const tcpMoved = saved === undefined || servicesAt !== servicesWere
  const udpAhead = able && opened && udpMoved
  const tcpAhead = able && opened && tcpMoved
  const udp = usePortState(draft.listenPort, draft.isEnabled && !udpAhead && !opening, "udp")
  const tcp = usePortState(servicesAt, draft.isEnabled && !tcpAhead && !opening)
  const running = saved !== undefined && saved.isEnabled ? saved : undefined
  const udpLabel = `${draft.listenPort}/udp`
  const tcpLabel = `${servicesAt}/tcp`
  const previous =
    opened && saved?.isEnabled === true && draft.isEnabled
      ? [
          ...(draft.listenPort !== start.listenPort ? [`${start.listenPort}/udp`] : []),
          ...(servicesAt !== servicesWere ? [`${servicesWere}/tcp`] : []),
        ]
      : []
  const socket = pathFault(t, draft.webSocketPath, draft.webSocket ? servicesAt : null, claimed)
  const name = nameFault(t, draft.name, others.map((one) => one.name))
  const address = addressFault(t, draft.address)
  const host = hostFault(t, draft)
  const edited = !same(draft, start) || opening
  const ready =
    edited &&
    !pending &&
    draft.name.length > 0 &&
    [port, services, socket, name, address, host].every((one) => one.length === 0)
  const twisted = JSON.stringify(draft.obfuscation) !== JSON.stringify(start.obfuscation)

  function put(change: Partial<ConfigDraft>) {
    setDraft({ ...draft, ...change })
  }

  // Saves, asking first whether the ports the panel held open before they moved are closed.
  function save() {
    if (previous.length > 0 && able && ufw) {
      setAsking(true)
      return
    }

    onSave(draft, opening ? { opened: true } : {})
  }

  function twist(change: Partial<Obfuscation>) {
    setDraft({ ...draft, obfuscation: { ...draft.obfuscation, ...change } })
  }

  async function pair() {
    const made = await keys.mutateAsync()
    setDraft({ ...draft, privateKey: made.privateKey })
    setShown(made.publicKey)
  }

  async function secret() {
    const made = await shared.mutateAsync()
    put({ presharedKey: made.key })
  }

  async function take() {
    const found = await read.mutateAsync({ name: draft.name, text })
    setDraft({
      ...draft,
      listenPort: found.listenPort,
      address: found.address,
      dns: found.dns,
      allowedIps: found.allowedIps,
      mtu: found.mtu,
      keepalive: found.keepalive,
      privateKey: found.privateKey ?? "",
      obfuscation: found.obfuscation,
    })
    setShown(found.publicKey)
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("configs.network")}>
        <div className="sm:col-span-2">
          <Switch
            id="config-on"
            caption={t("configs.enabled")}
            value={draft.isEnabled}
            onChange={(value) => put({ isEnabled: value })}
          />
        </div>
        <Line
          id="config-name"
          caption={t("configs.name")}
          value={draft.name}
          onChange={(value) => put({ name: value })}
          fault={name}
        />
        <Line
          id="config-host"
          caption={t("configs.host")}
          value={draft.host}
          onChange={(value) => put({ host: value })}
          fault={host}
        />
        <Count
          id="config-port"
          caption={t("configs.port")}
          value={draft.listenPort}
          onChange={(value) => put({ listenPort: value })}
          fault={port}
          note={
            draft.isEnabled && (
              <PortNote
                port={udpLabel}
                state={udp.data?.state}
                ahead={udpAhead}
                able={able}
                manual={manual(t, reach, udpLabel, "configs.portClosed")}
                owner={running !== undefined && !udpMoved ? { kind: "endpoint", id: running.id } : null}
                staged={opening}
                onStage={setOpening}
              />
            )
          }
        />
        <ListLine
          id="config-address"
          caption={t("configs.address")}
          value={draft.address}
          onChange={(address) => put({ address })}
          fault={address}
          wide
        />
        <Flag
          id="config-nat"
          caption={t("configs.nat")}
          value={draft.nat}
          onChange={(value) => put({ nat: value })}
        />
        <Pick
          id="config-inbound"
          caption={
            <span className="flex items-center gap-1">
              {t("configs.inbound")}
              <Help text={t("configs.inboundHint")} />
            </span>
          }
          value={draft.inbound}
          onChange={(value) => put({ inbound: value as Inbound })}
          wide
        >
          <option value="off">{t("clients.inboundOff")}</option>
          <option value="server">{t("clients.inboundServer")}</option>
          <option value="network">{t("clients.inboundNetwork")}</option>
        </Pick>
        <div className="sm:col-span-2">
          <Flag
            id="config-websocket"
            caption={t("configs.webSocket")}
            value={draft.webSocket}
            onChange={(value) => put({ webSocket: value })}
          />
          {proxy.length > 0 && <div className="mt-1 text-xs text-alarm">{proxy}</div>}
        </div>
        <Count
          id="config-services-port"
          caption={t("configs.servicesPort")}
          value={draft.servicesPort}
          unset={String(draft.listenPort)}
          onChange={(value) => put({ servicesPort: value })}
          fault={services}
          note={
            draft.isEnabled && (
              <PortNote
                port={tcpLabel}
                state={tcp.data?.state}
                ahead={tcpAhead}
                able={able}
                manual={manual(t, reach, tcpLabel, "configs.portClosed")}
                owner={running !== undefined && !tcpMoved ? { kind: "endpoint", id: running.id } : null}
                staged={opening}
                onStage={setOpening}
              />
            )
          }
        />
        <Line
          id="config-websocket-path"
          caption={t("configs.webSocketPath")}
          value={draft.webSocketPath}
          onChange={(value) => put({ webSocketPath: value.trim() })}
          fault={socket}
        />
      </Part>

      <Part title={t("configs.clients")}>
        <ListLine
          id="config-allowed"
          caption={t("configs.allowed")}
          value={draft.allowedIps}
          onChange={(allowedIps) => put({ allowedIps })}
          wide
        />
        <ListLine
          id="config-dns"
          caption={t("configs.dns")}
          value={draft.dns}
          onChange={(dns) => put({ dns })}
        />
        <Count id="config-mtu" caption={t("configs.mtu")} value={draft.mtu} onChange={(value) => put({ mtu: value })} />
        <Count
          id="config-keepalive"
          caption={t("configs.keepalive")}
          value={draft.keepalive}
          onChange={(value) => put({ keepalive: value })}
        />
        <Count
          id="config-online"
          caption={<Hinted caption={t("configs.offlineAfter")} text={t("configs.offlineAfterHint")} />}
          value={draft.offlineAfter}
          onChange={(value) => put({ offlineAfter: value })}
        />
        <ListLine
          id="config-blocked"
          caption={t("configs.blocked")}
          value={draft.blocked}
          onChange={(blocked) => put({ blocked })}
          wide
        />
      </Part>

      <Part title={t("configs.keys")}>
        <Line
          id="config-private"
          caption={t("configs.private")}
          value={draft.privateKey}
          onChange={(value) => put({ privateKey: value.trim() })}
          wide
        />
        <div className="sm:col-span-2 flex items-end gap-3">
          <div className="min-w-0 flex-1">
            <span className={label}>{t("configs.public")}</span>
            <div className={`mt-1 truncate ${field}`}>{shown}</div>
          </div>
          <button type="button" onClick={() => void pair()} disabled={keys.isPending} className={secondary}>
            {t("configs.newKeys")}
          </button>
        </div>

        <Line
          id="config-preshared"
          caption={t("configs.preshared")}
          value={draft.presharedKey}
          onChange={(value) => put({ presharedKey: value.trim() })}
          after={<Regenerate title={t("configs.generate")} disabled={shared.isPending} onClick={() => void secret()} />}
          wide
        />
      </Part>

      <Part title={t("configs.obfuscation")}>
        <ObfuscationFields id="config" cover={draft.obfuscation} onChange={twist} />
        {self !== undefined && twisted && (
          <div className="text-xs text-alarm sm:col-span-2">{t("configs.obfuscationWarning")}</div>
        )}
      </Part>

      {importable && (
        <Part title={t("configs.import")}>
          <div className="sm:col-span-2">
            <label className={label} htmlFor="config-file">
              {t("configs.paste")}
            </label>
            <textarea
              id="config-file"
              value={text}
              rows={5}
              onChange={(e) => setText(e.target.value)}
              className={`mt-1 font-mono text-xs ${field}`}
            />
          </div>
          <div className="sm:col-span-2 flex justify-end">
            <button
              type="button"
              onClick={() => void take()}
              disabled={read.isPending || text.trim().length === 0}
              className={secondary}
            >
              {t("configs.read")}
            </button>
          </div>
          {read.error !== null && read.error !== undefined && (
            <div className="text-sm text-alarm sm:col-span-2">{t(complaint(read.error) as TextKey)}</div>
          )}
        </Part>
      )}

      {said.length > 0 && proxy.length === 0 && <div className="text-sm text-alarm">{said}</div>}

      <div className={footer}>
        {onRemove !== undefined && (
          <button type="button" onClick={onRemove} className={`mr-auto ${danger}`}>
            {t("configs.remove")}
          </button>
        )}
        <button type="button" onClick={onClose} disabled={!edited || pending} className={secondary}>
          {t("configs.cancel")}
        </button>
        <button type="button" onClick={save} disabled={!ready} className={primary}>
          {pending ? t("configs.busy") : t("configs.save")}
        </button>
      </div>

      {asking && (
        <KeepQuestion
          ports={previous}
          onCancel={() => setAsking(false)}
          onAnswer={(keep) => {
            setAsking(false)
            onSave(draft, { keep })
          }}
        />
      )}
    </div>
  )
}

function nameFault(t: Text, name: string, taken: string[]): string {
  if (name.length === 0) {
    return ""
  }

  if (name.length > 15 || !/^[a-z][a-z0-9_-]*$/.test(name)) {
    return t("error.badInterfaceName")
  }

  return taken.includes(name) ? t("error.nameTaken") : ""
}

function hostFault(t: Text, draft: ConfigDraft): string {
  if (draft.host.length > 255) {
    return t("error.badHost")
  }

  return draft.isEnabled && draft.host.trim().length === 0 ? t("error.hostNeeded") : ""
}

function addressFault(t: Text, address: string[]): string {
  if (address.length === 0 || address.some((one) => span(one) === null)) {
    return t("error.badAddress")
  }

  return ""
}
