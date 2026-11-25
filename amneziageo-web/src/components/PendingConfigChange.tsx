import { reason } from "@/api/auth"
import { useState } from "react"
import { useApplyPendingConfig, useCancelPendingConfig, usePendingConfig, failure } from "@/api/configs"
import { Dialog } from "@/components/Dialog"
import { configChangeField } from "@/components/configChangeFields"
import { card, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function PendingConfigChange({ id }: { id: number }) {
  const t = useText()
  const pending = usePendingConfig(id)
  const apply = useApplyPendingConfig()
  const cancel = useCancelPendingConfig()
  const [asking, setAsking] = useState(false)
  const change = pending.data
  const busy = apply.isPending || cancel.isPending
  if (!change) return null
  return <section className={`mt-4 p-4 ${card}`}>
    <div className="flex flex-wrap items-center justify-between gap-3">
      <span>{t("configs.pendingChange")} · {change.name}</span>
      <div className="flex gap-2">
        <button className={secondary} disabled={busy} onClick={() => cancel.mutate(id)}>{t("configs.discardChange")}</button>
        <button className={primary} disabled={busy || change.stale} onClick={() => setAsking(true)}>{t("configs.applyNow")}</button>
      </div>
    </div>
    {change.preview.addressFault ? <p role="alert" className="mt-2 text-alarm">{t(reason(change.preview.addressFault.code))}</p> : change.stale && <p role="alert" className="mt-2 text-alarm">{t("error.configChangeStale")}</p>}
    {(apply.error || cancel.error) && <p role="alert" className="mt-2 text-alarm">{failure(t, apply.error || cancel.error)}</p>}
    {asking && <Dialog title={t("configs.applyChangeTitle")} onClose={() => { if (!busy) setAsking(false) }} actions={<>
      <button className={secondary} disabled={busy} onClick={() => setAsking(false)}>{t("configs.cancel")}</button>
      <button className={primary} disabled={busy} onClick={() => apply.mutate(id, { onSuccess: () => setAsking(false) })}>{t("configs.applyNow")}</button>
    </>}>
      <div>{t("configs.interfaceChange", { name: change.name })}</div>
      <div>{t("configs.affectedClients", { count: change.preview.clients.length })}</div>
      {change.preview.clients.some(one => one.requiresCoordination) && <div className="mt-2 text-alarm">{t("configs.connectionChangeWarning")}</div>}
      <div className="mt-2 max-h-56 overflow-auto">{change.preview.clients.map(one => <div key={one.id} className="break-words">{one.name}: {one.fields.map(field => configChangeField(t, field)).join(", ")}</div>)}</div>
      {apply.error && <div role="alert" className="text-alarm">{failure(t, apply.error)}</div>}
    </Dialog>}
  </section>
}
