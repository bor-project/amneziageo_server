import { useRef, useState } from "react"
import { useEditorExit } from "@/components/EditorExit"
import { complaint } from "@/api/auth"
import type { Obfuscation } from "@/api/configs"
import { useImportOutbound, useOutboundKeys, useOutbounds } from "@/api/outbounds"
import { useConfigs } from "@/api/configs"
import { useBalancers } from "@/api/balancers"
import { interfaceNameFault, interfaceNameValue } from "@/components/interfaceNames"
import type { OutboundDraft, OutboundKind } from "@/api/outbounds"
import { WayUse } from "@/components/Holders"
import { ObfuscationFields } from "@/components/Obfuscation"
import { headerProtectionMtu } from "@/components/headerProtection"
import { Count, Flag, Line, ListLine, Part, Switch } from "@/components/fields"
import { danger, field, footer, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { same } from "@/store/draftSlice"

export function OutboundForm({
  start,
  publicKey,
  self,
  pending,
  error,
  onSave,
  onSaved,
  onClose,
  onRemove,
}: {
  start: OutboundDraft
  publicKey: string
  self?: string
  pending: boolean
  error: unknown
  onSave: (draft: OutboundDraft) => Promise<unknown>
  onSaved: (draft: OutboundDraft) => void
  onClose: () => void
  onRemove?: () => void
}) {
  const t = useText()
  const keys = useOutboundKeys()
  const read = useImportOutbound()
  const nameCustomized = useRef(self !== undefined)
  const nameChanges = useRef(0)
  const outbounds = useOutbounds().data ?? []
  const configs = useConfigs().data ?? []
  const balancers = useBalancers().data ?? []
  const [draft, setDraft] = useState(start)
  const [shown, setShown] = useState(publicKey)
  const [text, setText] = useState("")
  const [readingFile, setReadingFile] = useState(false)
  const [fileFailed, setFileFailed] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)
  const reading = readingFile || read.isPending
  const edited = !same(draft, start)
  const { leave, prompt } = useEditorExit(edited || text.length > 0, pending || reading)
  const name = interfaceNameFault(t, draft.name, [...outbounds.filter((one) => one.name !== self).map((one) => one.name), ...balancers.map((one) => one.name), ...(self !== undefined && interfaceNameValue(draft.name) === start.name ? [] : configs.map((one) => one.name))])

  function put(change: Partial<OutboundDraft>) {
    setDraft({ ...draft, ...change })
  }

  function twist(change: Partial<Obfuscation>, generated = false) {
    setDraft({
      ...draft,
      mtu: generated ? headerProtectionMtu(draft.mtu, draft.obfuscation.headerProtectionKey) : draft.mtu,
      obfuscation: { ...draft.obfuscation, ...change },
    })
  }

  async function pair() {
    const made = await keys.mutateAsync()
    setDraft({ ...draft, privateKey: made.privateKey })
    setShown(made.publicKey)
  }

  async function take(config: string) {
    const revision = nameChanges.current
    const found = await read.mutateAsync({ name: nameCustomized.current ? draft.name : "", config }).catch(() => null)
    if (found === null) {
      return
    }
    setDraft((current) => ({
      ...current,
      name: nameChanges.current === revision ? found.name : current.name,
      kind: found.kind,
      host: found.host,
      port: found.port,
      privateKey: found.privateKey ?? "",
      peerKey: found.peerKey,
      presharedKey: found.presharedKey ?? "",
      address: found.address,
      dns: found.dns ?? [],
      mtu: found.mtu,
      keepalive: found.keepalive,
      obfuscation: found.obfuscation,
    }))
    setShown(found.publicKey)
    setText("")
  }

  async function takeFile(file: File | undefined) {
    if (file === undefined) {
      return
    }

    setReadingFile(true)
    setFileFailed(false)
    read.reset()
    try {
      const config = await file.text()
      setText(config)
      await take(config)
    } catch {
      setFileFailed(true)
    } finally {
      setReadingFile(false)
    }
  }

  const tunnel = draft.kind === "wg" || draft.kind === "ws"

  return (
    <div className="mt-4 flex flex-col gap-4">
      {tunnel && (
        <Part title={t("outbounds.import")}>
          <div className="sm:col-span-2">
            <label className={label} htmlFor="outbound-config">{t("outbounds.paste")}</label>
            <textarea
              id="outbound-config"
              value={text}
              rows={5}
              autoComplete="off"
              spellCheck={false}
              onChange={(event) => {
                setText(event.target.value)
                setFileFailed(false)
                read.reset()
              }}
              disabled={pending || reading}
              className={`mt-1 font-mono text-xs ${field}`}
            />
          </div>
          <div className="sm:col-span-2 flex flex-wrap justify-end gap-2">
            <input
              ref={fileInput}
              type="file"
              accept=".conf,.ini,.txt,text/plain"
              hidden
              onChange={(event) => {
                const file = event.currentTarget.files?.[0]
                event.currentTarget.value = ""
                void takeFile(file)
              }}
            />
            <button type="button" onClick={() => fileInput.current?.click()} disabled={pending || reading} className={secondary}>
              {t("outbounds.configFile")}
            </button>
            <button type="button" onClick={() => void take(text)} disabled={pending || reading || text.trim().length === 0} className={secondary}>
              {t("outbounds.read")}
            </button>
          </div>
          {fileFailed && <div className="text-sm text-alarm sm:col-span-2" role="alert">{t("outbounds.configFileError")}</div>}
          {read.error != null && <div className="text-sm text-alarm sm:col-span-2" role="alert">{t(complaint(read.error) as TextKey)}</div>}
        </Part>
      )}
      <Part title={t("outbounds.settings")}>
        <div className="sm:col-span-2">
          <Switch
            id="outbound-enabled"
            caption={t("outbounds.enabled")}
            value={draft.isEnabled}
            onChange={(value) => put({ isEnabled: value })}
          />
        </div>
        <Line
          id="outbound-name"
          caption={t("outbounds.name")}
          value={draft.name}
          onChange={(value) => {
            nameCustomized.current = true
            nameChanges.current++
            put({ name: value })
          }}
          fault={name}
        />
        <div>
          <label className={label} htmlFor="outbound-kind">
            {t("outbounds.kind")}
          </label>
          <select
            id="outbound-kind"
            value={draft.kind}
            onChange={(e) => put({ kind: e.target.value as OutboundKind })}
            className={`mt-1 ${field}`}
          >
            {draft.kind === "local" && <option value="local">{t("rules.actionDirect")}</option>}
            <option value="wg">{t("outbounds.kindWg")}</option>
            <option value="ws">{t("outbounds.kindWs")}</option>
          </select>
        </div>
      </Part>

      {self !== undefined && (
        <WayUse
          name={self}
          balancers={draft.balancers}
          onBalancers={(balancers) => put({ balancers })}
        />
      )}

      <Part title={t("outbounds.probe")}>
        <Line
          id="outbound-probe"
          caption={t("outbounds.probeServer")}
          value={draft.probe}
          onChange={(value) => put({ probe: value })}
        />
        <Count
          id="outbound-probe-every"
          caption={t("outbounds.probeEvery")}
          value={draft.probeEvery}
          onChange={(value) => put({ probeEvery: value })}
        />
      </Part>

      {tunnel && (
        <Part title={t("outbounds.server")}>
          <Line
            id="outbound-host"
            caption={t("outbounds.host")}
            value={draft.host}
            onChange={(value) => put({ host: value })}
          />
          <Count
            id="outbound-port"
            caption={t("outbounds.port")}
            value={draft.port}
            onChange={(value) => put({ port: value })}
          />
          {draft.kind === "ws" && (
            <Line
              id="outbound-proxy"
              caption={t("outbounds.proxy")}
              value={draft.proxy}
              onChange={(value) => put({ proxy: value })}
              wide
            />
          )}
          <ListLine
            id="outbound-address"
            caption={t("outbounds.address")}
            value={draft.address}
            onChange={(address) => put({ address })}
            wide
          />
          <Count
            id="outbound-mtu"
            caption={t("outbounds.mtu")}
            value={draft.mtu}
            onChange={(value) => put({ mtu: value })}
          />
          <Count
            id="outbound-keepalive"
            caption={t("outbounds.keepalive")}
            value={draft.keepalive}
            onChange={(value) => put({ keepalive: value })}
          />
          <Flag
            id="outbound-close-private"
            caption={t("outbounds.closePrivate")}
            value={draft.closePrivate}
            onChange={(value) => put({ closePrivate: value })}
          />
        </Part>
      )}

      <Part title={t("tab.dns")}>
        <div className="sm:col-span-2"><ListLine id="outbound-dns" caption={t("routing.serverDns")} value={draft.dns} onChange={(dns) => put({ dns })} /></div>
      </Part>

      {tunnel && (
        <Part title={t("outbounds.keys")}>
          <Line
            id="outbound-private"
            caption={t("outbounds.private")}
            value={draft.privateKey}
            onChange={(value) => put({ privateKey: value.trim() })}
            wide
          />
          <div className="sm:col-span-2 flex items-end gap-3">
            <div className="min-w-0 flex-1">
              <span className={label}>{t("outbounds.public")}</span>
              <div className={`mt-1 truncate ${field}`}>{shown}</div>
            </div>
            <button type="button" onClick={() => void pair()} disabled={keys.isPending} className={secondary}>
              {t("outbounds.newKeys")}
            </button>
          </div>
          <Line
            id="outbound-peer"
            caption={t("outbounds.peerKey")}
            value={draft.peerKey}
            onChange={(value) => put({ peerKey: value.trim() })}
            wide
          />
          <Line
            id="outbound-preshared"
            caption={t("outbounds.preshared")}
            value={draft.presharedKey}
            onChange={(value) => put({ presharedKey: value.trim() })}
            wide
          />
        </Part>
      )}

      {tunnel && (
        <Part title={t("outbounds.obfuscation")}>
          <ObfuscationFields id="outbound" cover={draft.obfuscation} onChange={twist} />
        </Part>
      )}


      {error !== null && error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(error) as TextKey)}</div>
      )}

      <div className={footer}>
        {onRemove !== undefined && (
          <button type="button" onClick={onRemove} className={`mr-auto ${danger}`}>
            {t("outbounds.remove")}
          </button>
        )}
        <button type="button" onClick={onClose} disabled={pending} className={secondary}>
          {t("outbounds.cancel")}
        </button>
        <button
          type="button"
          onClick={() => void onSave(draft).then(() => leave(() => onSaved(draft))).catch(() => undefined)}
          disabled={!edited || pending || reading || name.length > 0}
          className={primary}
        >
          {pending ? t("outbounds.busy") : t("outbounds.save")}
        </button>
      </div>
      {prompt}
    </div>
  )
}
