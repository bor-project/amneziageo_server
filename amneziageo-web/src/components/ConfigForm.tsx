import { useState } from "react"
import { span } from "@/address"
import { complaint } from "@/api/auth"
import { failure, useConfigs, useImportConfig, useKeyPair, usePresharedKey } from "@/api/configs"
import type { ConfigDraft, Obfuscation } from "@/api/configs"
import type { Inbound } from "@/api/clients"
import { ObfuscationFields } from "@/components/Obfuscation"
import { Count, Flag, Help, Line, Part, Pick, Switch } from "@/components/fields"
import { portFault, usePortHolders } from "@/components/ports"
import { card, danger, field, label, primary, secondary } from "@/components/styles"
import { parts } from "@/format"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"

export function ConfigForm({
  start,
  publicKey,
  self,
  pending,
  error,
  fault = "",
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
  onSave: (draft: ConfigDraft) => void
  onClose: () => void
  onRemove?: () => void
  importable?: boolean
}) {
  const t = useText()
  const keys = useKeyPair()
  const shared = usePresharedKey()
  const others = (useConfigs().data ?? []).filter((one) => one.id !== self)
  const held = usePortHolders({ config: self })
  const [draft, setDraft] = useState(start)
  const [shown, setShown] = useState(publicKey)
  const read = useImportConfig()
  const [text, setText] = useState("")
  const said = error !== null && error !== undefined ? failure(t, error) : fault
  const port = portFault(t, draft.listenPort, held)
  const services = draft.servicesPort === 0 ? "" : portFault(t, draft.servicesPort, held)
  const name = nameFault(t, draft.name, others.map((one) => one.name))
  const address = addressFault(t, draft.address)
  const host = draft.host.length > 255 ? t("error.badHost") : ""
  const ready =
    !pending && draft.name.length > 0 && [port, services, name, address, host].every((one) => one.length === 0)
  const twisted = JSON.stringify(draft.obfuscation) !== JSON.stringify(start.obfuscation)

  function put(change: Partial<ConfigDraft>) {
    setDraft({ ...draft, ...change })
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
        />
        {port.length > 0 && <div className="-mt-2 text-xs text-alarm">{port}</div>}
        <Line
          id="config-address"
          caption={t("configs.address")}
          value={draft.address.join(", ")}
          onChange={(value) => put({ address: parts(value) })}
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
        <Flag
          id="config-websocket"
          caption={t("configs.webSocket")}
          value={draft.webSocket}
          onChange={(value) => put({ webSocket: value })}
        />
        <div>
          <Count
            id="config-services-port"
            caption={t("configs.servicesPort")}
            value={draft.servicesPort}
            unset={String(draft.listenPort)}
            onChange={(value) => put({ servicesPort: value })}
          />
          {services.length > 0 && <div className="mt-1 text-xs text-alarm">{services}</div>}
        </div>
      </Part>

      <Part title={t("configs.clients")}>
        <Line
          id="config-allowed"
          caption={t("configs.allowed")}
          value={draft.allowedIps.join(", ")}
          onChange={(value) => put({ allowedIps: parts(value) })}
          wide
        />
        <Line
          id="config-dns"
          caption={t("configs.dns")}
          value={draft.dns.join(", ")}
          onChange={(value) => put({ dns: parts(value) })}
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
          caption={t("configs.offlineAfter")}
          value={draft.offlineAfter}
          onChange={(value) => put({ offlineAfter: value })}
          hint={t("configs.offlineAfterHint")}
        />
        <Line
          id="config-blocked"
          caption={t("configs.blocked")}
          value={draft.blocked.join(", ")}
          onChange={(value) => put({ blocked: parts(value) })}
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

        <div className="sm:col-span-2 flex items-end gap-3">
          <div className="min-w-0 flex-1">
            <label className={label} htmlFor="config-preshared">
              {t("configs.preshared")}
            </label>
            <input
              id="config-preshared"
              value={draft.presharedKey}
              onChange={(e) => put({ presharedKey: e.target.value.trim() })}
              className={`mt-1 ${field}`}
            />
          </div>
          <button type="button" onClick={() => void secret()} disabled={shared.isPending} className={secondary}>
            {t("configs.generate")}
          </button>
        </div>
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

      {said.length > 0 && <div className="text-sm text-alarm">{said}</div>}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        {onRemove !== undefined && (
          <button type="button" onClick={onRemove} className={`mr-auto ${danger}`}>
            {t("configs.remove")}
          </button>
        )}
        <button type="button" onClick={onClose} className={secondary}>
          {t("configs.cancel")}
        </button>
        <button type="button" onClick={() => onSave(draft)} disabled={!ready} className={primary}>
          {pending ? t("configs.busy") : t("configs.save")}
        </button>
      </div>
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

function addressFault(t: Text, address: string[]): string {
  if (address.length === 0 || address.some((one) => span(one) === null)) {
    return t("error.badAddress")
  }

  return ""
}
