import { useState } from "react"
import { Navigate, useLocation, useNavigate, useParams } from "react-router-dom"
import { downedOf, draftOf, failure, useAddConfig, useChangeConfig, useConfigPreview, useConfigs, useFreshConfig, usePendingConfig } from "@/api/configs"
import type { ConfigChangePreview, ConfigDraft } from "@/api/configs"
import type { Holding } from "@/api/firewall"
import { Dialog } from "@/components/Dialog"
import { configChangeField } from "@/components/configChangeFields"
import { PendingConfigChange } from "@/components/PendingConfigChange"
import { primary, secondary } from "@/components/styles"
import type { Config } from "@/api/configs"
import { scopes } from "@/api/scopes"
import { ConfigForm } from "@/components/ConfigForm"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"
import { useSpot } from "@/store/spots"

export function ConfigPage() {
  const { configId } = useParams()

  return configId === undefined ? <NewConfig /> : <HeldConfig configId={Number(configId)} />
}

function NewConfig() {
  const t = useText()
  const navigate = useNavigate()
  const configs = useConfigs()
  const fresh = useFreshConfig(configs.data !== undefined, nextName(configs.data))
  const add = useAddConfig()
  const back = useSpot("/interfaces")

  useTail([{ label: t("configs.newTitle") }])

  if (fresh.data === undefined) {
    return <div className="mt-4 text-sm text-muted">{t("configs.loading")}</div>
  }

  return (
    <ConfigForm
      start={draftOf(fresh.data)}
      publicKey={fresh.data.publicKey}
      pending={add.isPending}
      error={add.error}
      onSave={(draft, holding) =>
        void add.mutateAsync({ ...draft, ...holding }).then(
          () => navigate(back),
          (error: unknown) => {
            const downed = downedOf(error)
            if (downed !== null) {
              navigate(`/interfaces/${downed.id}/edit`, {
                state: { fault: failure(t, error), of: downed.error },
              })
            }
          },
        )
      }
      onClose={() => navigate(back)}
      importable
    />
  )
}

function HeldConfig({ configId }: { configId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const { state } = useLocation()
  const configs = useConfigs()
  const change = useChangeConfig()
  const preview = useConfigPreview()
  const may = holds(user, scopes.manageInterfaces)
  const pending = usePendingConfig(configId, may)
  const [proposed, setProposed] = useState<{ draft: ConfigDraft & Holding; preview: ConfigChangePreview; draftRevision?: string } | null>(null)
  const [round, setRound] = useState(0)
  const arrived = (state as { fault?: string } | null)?.fault ?? ""
  const arrivedOf = (state as { of?: string } | null)?.of ?? ""
  const all = configs.data ?? []
  const held = all.find((one) => one.id === configId)
  const back = useSpot("/interfaces")

  useTail(held === undefined ? [] : [{ label: held.name }])

  if (held === undefined) {
    return configs.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("configs.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  function save(draft: ConfigDraft & Holding, impact?: ConfigChangePreview, defer = false, draftRevision?: string) {
    void change.mutateAsync({ id: configId, draft: { ...draft, defer, expectedRevision: impact?.baseRevision, expectedClientsRevision: impact?.clientsRevision, expectedDraftRevision: draftRevision } }).then(
      () => navigate(back),
      (error: unknown) => { setProposed(null); if (downedOf(error) !== null) setRound(one => one + 1) },
    )
  }

  return (<>
    {may && <PendingConfigChange id={configId} />}
    {preview.error && <div role="alert" className="mt-3 text-alarm">{failure(t, preview.error)}</div>}
    <ConfigForm
      key={round}
      start={draftOf(held)}
      publicKey={held.publicKey}
      self={held.id}
      pending={change.isPending || preview.isPending}
      error={change.error}
      fault={change.error === null ? arrived : ""}
      faultOf={change.error === null ? arrivedOf : ""}
      onSave={(draft, holding) => {
        const submitted = { ...draft, ...holding }
        void preview.mutateAsync({ id: held.id, draft: submitted }).then(impact => {
          if (impact.clients.length > 0 || pending.data) setProposed({ draft: submitted, preview: impact, draftRevision: pending.data?.draftRevision })
          else save(submitted, impact)
        }, () => {})
      }}
      onClose={() => navigate(back)}
      onRemove={may ? () => navigate(`/interfaces/${held.id}/delete`) : undefined}
    />
    {proposed && <Dialog title={t("configs.applyChangeTitle")} onClose={() => { if (!change.isPending) setProposed(null) }} actions={<>
      <button className={secondary} disabled={change.isPending} onClick={() => setProposed(null)}>{t("configs.cancel")}</button>
      <button className={secondary} disabled={change.isPending} onClick={() => save(proposed.draft, proposed.preview, true, proposed.draftRevision)}>{t("configs.applyLater")}</button>
      <button className={primary} disabled={change.isPending} onClick={() => save(proposed.draft, proposed.preview, false, proposed.draftRevision)}>{t("configs.applyNow")}</button>
    </>}>
      <div>{t("configs.interfaceChange", { name: held.name })}</div>
      {proposed.draftRevision && <div className="mt-2">{t("configs.replacePendingChange")}</div>}
      <div>{t("configs.affectedClients", { count: proposed.preview.clients.length })}</div>
      {proposed.preview.clients.some(one => one.requiresCoordination) && <div className="mt-2 text-alarm">{t("configs.connectionChangeWarning")}</div>}
      <div className="mt-2 max-h-56 overflow-auto">{proposed.preview.clients.map(one => <div key={one.id} className="break-words">{one.name}: {one.fields.map(field => configChangeField(t, field)).join(", ")}</div>)}</div>
    </Dialog>}
  </>)
}

function nextName(configs: Config[] | undefined): string {
  const taken = new Set((configs ?? []).map((one) => one.name))
  for (let number = 1; number < 100; number++) {
    if (!taken.has(`awg${number}`)) {
      return `awg${number}`
    }
  }

  return "awg0"
}
