import { useState } from "react"
import { useNavigate } from "react-router-dom"
import { complaint } from "@/api/auth"
import { scopes } from "@/api/scopes"
import { draftOf, useChangeDefaultTemplate, useDefaultTemplate, useRefreshDefaultTemplate, useResetDefaultTemplate } from "@/api/templates"
import { Dialog } from "@/components/Dialog"
import { HeadActions } from "@/components/Section"
import { TemplateForm } from "@/components/TemplateForm"
import { useTail } from "@/components/crumbs"
import { primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"
import { useSpot } from "@/store/spots"

export function DefaultTemplate() {
  const t = useText()
  const navigate = useNavigate()
  const defaults = useDefaultTemplate()
  const change = useChangeDefaultTemplate()
  const reset = useResetDefaultTemplate()
  const refresh = useRefreshDefaultTemplate()
  const back = useSpot("/clients/templates")
  const may = holds(useAppSelector((s) => s.auth.user), scopes.manageClients)
  const [confirmReset, setConfirmReset] = useState(false)
  const [revision, setRevision] = useState(0)
  const held = defaults.data
  const busy = change.isPending || reset.isPending || refresh.isPending

  useTail([{ label: t("clients.noTemplate") }])

  if (held === undefined) {
    return <div className="mt-4 text-sm text-muted">{defaults.error === null ? t("templates.loading") : t(complaint(defaults.error))}</div>
  }

  return (
    <div>
      {may && held.entries.length > 0 && (
        <HeadActions>
          <button type="button" onClick={() => refresh.mutate()} disabled={busy} className={`flex h-10 items-center ${secondary}`}>
            {refresh.isPending ? t("templates.refreshing") : t("templates.refresh")}
          </button>
        </HeadActions>
      )}
      <TemplateForm
        key={`${held.updatedUtc}:${revision}`}
        start={draftOf(held)}
        held={held}
        fixedName
        readOnly={!may}
        pending={busy}
        error={change.error ?? reset.error ?? refresh.error}
        onSave={(draft) => void change.mutateAsync(draft).then(() => navigate(back)).catch(() => undefined)}
        onClose={() => navigate(back)}
        onReset={() => setConfirmReset(true)}
      />
      {confirmReset && (
        <Dialog
          title={t("templates.resetConfirm")}
          onClose={() => { if (!reset.isPending) { setConfirmReset(false) } }}
          actions={<>
            <button type="button" onClick={() => setConfirmReset(false)} disabled={reset.isPending} className={secondary}>{t("templates.cancel")}</button>
            <button type="button" onClick={() => void reset.mutateAsync().then(() => { setConfirmReset(false); setRevision((value) => value + 1) }).catch(() => undefined)} disabled={reset.isPending} className={primary}>{t("templates.reset")}</button>
          </>}
        >
          {reset.error !== null && <div className="text-alarm">{t(complaint(reset.error))}</div>}
        </Dialog>
      )}
    </div>
  )
}
