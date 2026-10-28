import { useState } from "react"
import { complaint, reason } from "@/api/auth"
import {
  address,
  draftOf,
  freshToken,
  own,
  useCheckClientUpdates,
  useClientUpdates,
  useSaveClientUpdates,
} from "@/api/clientUpdates"
import type { ClientUpdateDraft, ClientUpdates as Settings } from "@/api/clientUpdates"
import { useConfigs } from "@/api/configs"
import { useFailure } from "@/api/failure"
import { scopes } from "@/api/scopes"
import { ReleaseList } from "@/components/ReleaseList"
import { Count, Flag, Line, Part, Regenerate, Shown, Switch } from "@/components/fields"
import { socketFault } from "@/components/ports"
import { footerUnder, label, note, primary, secondary } from "@/components/styles"
import { bytes } from "@/format"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { randomId } from "@/keys"
import { copyText } from "@/qr"
import { holds } from "@/store/authSlice"
import { clientUpdatesDrafted, same } from "@/store/draftSlice"
import { useAppDispatch, useAppSelector } from "@/store/hooks"

export function ClientUpdates() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const updates = useClientUpdates()
  const failed = useFailure(updates)
  const may = holds(user, scopes.manageClientUpdates)

  if (updates.data === undefined) {
    return failed !== null ? (
      <div className="mt-4 text-sm text-alarm">{t(complaint(failed))}</div>
    ) : (
      <div className="mt-4 text-sm text-muted">{t("page.loading")}</div>
    )
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Editor settings={updates.data} may={may} />
      {updates.data.isEnabled && (
        <div className="mt-5">
          <ReleaseList may={may} />
        </div>
      )}
    </div>
  )
}

function Editor({ settings, may }: { settings: Settings; may: boolean }) {
  const t = useText()
  const dispatch = useAppDispatch()
  const kept = useAppSelector((s) => s.drafts.clientUpdates)
  const [fault, setFault] = useState<TextKey | null>(null)
  const [copied, setCopied] = useState(false)
  const save = useSaveClientUpdates()
  const check = useCheckClientUpdates()
  const saved = draftOf(settings)
  const draft = kept ?? saved
  const owned = draft.source === own
  const configs = useConfigs().data ?? []
  const port = draft.port > 0 ? draft.port : settings.panelPort
  const socket =
    draft.isEnabled && owned ? socketFault(t, configs, port === settings.panelPort ? null : port, draft.path) : ""
  const tokenMissing = !owned && saved.source !== address && draft.token.length === 0
  const refused = fault ?? (kept === null && settings.fault.length > 0 ? reason(settings.fault) : null)
  const live = kept === null && settings.isEnabled

  function set(change: Partial<ClientUpdateDraft>) {
    const next = { ...draft, ...change }
    dispatch(clientUpdatesDrafted(same(next, saved) ? null : next))
    setCopied(false)
  }

  // Takes the source and the token that goes with it: the token of a store of the panel is not handed to a service.
  function pickSource(source: string) {
    if (source === saved.source) {
      set({ source, token: saved.token })
      return
    }

    set({ source, token: "" })
  }

  async function keep() {
    setFault(null)
    try {
      await save.mutateAsync(draft)
      dispatch(clientUpdatesDrafted(null))
    } catch (error) {
      setFault(complaint(error))
    }
  }

  function drop() {
    setFault(null)
    dispatch(clientUpdatesDrafted(null))
  }

  async function copy() {
    setCopied(await copyText(draft.token))
  }

  return (
    <div className="flex flex-col gap-4">
      <Part title={t("clientUpdates.partServing")}>
        <div className="sm:col-span-2">
          <Switch
            id="client-updates-enabled"
            caption={t("clientUpdates.enabled")}
            value={draft.isEnabled}
            onChange={(isEnabled) => set({ isEnabled })}
          />
        </div>

        <div className="sm:col-span-2">
          <div className={label}>{t("clientUpdates.source")}</div>
          <div className="mt-1.5 flex flex-wrap gap-5">
            {[own, address].map((one) => (
              <label key={one} className="flex items-center gap-2 text-sm text-ink" htmlFor={`client-updates-source-${one}`}>
                <input
                  id={`client-updates-source-${one}`}
                  type="radio"
                  name="client-updates-source"
                  checked={draft.source === one}
                  onChange={() => pickSource(one)}
                  className="size-4 accent-brand"
                />
                {t(one === own ? "clientUpdates.own" : "clientUpdates.address")}
              </label>
            ))}
          </div>
        </div>
      </Part>

      {owned ? (
        <Part title={t("clientUpdates.own")}>
          <div className="sm:col-span-2">
            <Line
              id="client-updates-directory"
              caption={t("clientUpdates.directory")}
              value={draft.directory}
              placeholder={settings.defaultDirectory}
              onChange={(directory) => set({ directory })}
            />
            {live && settings.source === own && <StoreLine settings={settings} />}
          </div>

          <Count
            id="client-updates-port"
            caption={t("settings.port")}
            value={draft.port}
            unset={String(settings.panelPort)}
            onChange={(value) => set({ port: value })}
          />

          <Line
            id="client-updates-path"
            caption={t("clientUpdates.path")}
            value={draft.path}
            onChange={(path) => set({ path })}
            fault={socket}
            after={<Regenerate title={t("clientUpdates.newPath")} onClick={() => set({ path: randomId() })} />}
          />

          <div className="sm:col-span-2">
            <label className={label} htmlFor="client-updates-token">
              {t("clientUpdates.token")}
            </label>
            <div className="mt-1 flex gap-2">
              <input
                id="client-updates-token"
                value={masked(draft.token)}
                readOnly
                className="min-w-0 flex-1 rounded-lg border border-line-input bg-input px-3 py-2.25 font-mono text-sm text-ink outline-none"
              />
              <button type="button" className={secondary} disabled={draft.token.length === 0} onClick={() => void copy()}>
                {copied ? t("clientUpdates.copied") : t("clientUpdates.copy")}
              </button>
              <Regenerate title={t("clientUpdates.newToken")} onClick={() => set({ token: freshToken() })} />
            </div>
          </div>

          {draft.path.length > 0 && (
            <Shown id="client-updates-url" caption={t("clientUpdates.url")} value={urlOf(port, draft.path)} wide />
          )}

          <div className="sm:col-span-2">
            <Flag
              id="client-updates-default"
              caption={t("clientUpdates.default")}
              value={draft.default}
              onChange={(value) => set({ default: value })}
            />
          </div>
        </Part>
      ) : (
        <Part title={t("clientUpdates.address")}>
          <Line
            id="client-updates-address"
            caption={t("clientUpdates.serviceAddress")}
            value={draft.address}
            placeholder="https://updates.example.net/"
            onChange={(value) => set({ address: value.trim() })}
            wide
          />

          <Line
            id="client-updates-service-token"
            caption={t("clientUpdates.token")}
            value={draft.token}
            placeholder={saved.source === address ? "••••••••" : ""}
            onChange={(token) => set({ token: token.trim() })}
            fault={tokenMissing && draft.address.length > 0 ? t("clientUpdates.tokenNeeded") : ""}
          />

          <div className="flex flex-wrap items-end gap-3 pb-0.5">
            <button
              type="button"
              className={secondary}
              disabled={!live || settings.source !== address || check.isPending}
              onClick={() => void check.mutateAsync()}
            >
              {check.isPending ? t("clientUpdates.checking") : t("clientUpdates.check")}
            </button>
            {live && settings.source === address && <ServiceLine settings={settings} />}
          </div>

          <div className="sm:col-span-2">
            <Flag
              id="client-updates-default"
              caption={t("clientUpdates.default")}
              value={draft.default}
              onChange={(value) => set({ default: value })}
            />
          </div>
        </Part>
      )}

      {refused !== null && (
        <div className="text-sm text-alarm">
          {t(refused)}
          {kept === null && settings.detail.length > 0 && <span className="text-muted"> ({settings.detail})</span>}
        </div>
      )}

      <div className={footerUnder}>
        <button type="button" className={secondary} disabled={kept === null || save.isPending} onClick={drop}>
          {t("settings.cancel")}
        </button>
        <button
          type="button"
          className={primary}
          disabled={!may || kept === null || save.isPending || socket.length > 0 || tokenMissing}
          onClick={() => void keep()}
        >
          {t("settings.save")}
        </button>
      </div>
    </div>
  )
}

// The room and the access of the directory of the store of the panel.
function StoreLine({ settings }: { settings: Settings }) {
  const t = useText()
  const status = settings.status
  if (status === null) {
    return null
  }

  if (!status.writable) {
    return <div className="mt-1 text-xs text-alarm">{`✗ ${status.fault}`}</div>
  }

  return (
    <div className={note}>
      {`${t("clientUpdates.free", { size: bytes(t, status.freeBytes) })} · `}
      <span className="text-good">✓</span>
      {` ${t("clientUpdates.writable")}`}
    </div>
  )
}

// Whether the service apart answered when it was last asked, and what it holds.
function ServiceLine({ settings }: { settings: Settings }) {
  const t = useText()
  if (settings.addressFault.length > 0) {
    return <span className="pb-2 text-sm text-alarm">{`✗ ${t("clientUpdates.silent", { why: settings.addressFault })}`}</span>
  }

  if (settings.status === null) {
    return null
  }

  return (
    <span className="pb-2 text-sm text-muted">
      <span className="text-good">✓</span>
      {` ${t("clientUpdates.answers", { count: settings.status.releases, size: bytes(t, settings.status.bytes) })}`}
    </span>
  )
}

function masked(token: string): string {
  return token.length > 8 ? `${token.slice(0, 4)}…${token.slice(-2)}` : token
}

// The address the clients read the releases at through the host the panel is open on.
function urlOf(port: number, path: string): string {
  const host = window.location.hostname
  const named = host.includes(":") ? `[${host}]` : host

  return `https://${named}:${port}/${path.replace(/^\/+|\/+$/g, "")}/`
}
