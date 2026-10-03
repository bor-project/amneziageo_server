import axios from "axios"
import { complaint } from "@/api/auth"
import { useOpenPort } from "@/api/firewall"
import type { PortOwner, PortState } from "@/api/firewall"
import { Dialog } from "@/components/Dialog"
import { primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"

const button =
  "rounded-md border border-line-button bg-surface px-2 py-0.5 text-xs font-medium text-ink-soft hover:bg-hover hover:text-ink disabled:opacity-50"

const link = "text-xs text-brand-ink hover:text-brand-lit"

// The line under a port field that names a closed port and opens it: at once for the port that is saved, as the form
// is saved for a port that is new.
export function PortNote({
  port,
  state,
  ahead = false,
  able,
  manual,
  owner = null,
  staged = false,
  onStage,
}: {
  port: string
  state: PortState | undefined
  ahead?: boolean
  able: boolean
  manual: string
  owner?: PortOwner | null
  staged?: boolean
  onStage?: (on: boolean) => void
}) {
  const t = useText()
  const open = useOpenPort()

  if (ahead || staged) {
    return (
      <div className="mt-1 flex flex-wrap items-center gap-x-2 text-xs text-muted">
        <span>{t("ports.opensOnSave", { port })}</span>
        {staged && onStage !== undefined && (
          <button type="button" onClick={() => onStage(false)} className={link}>
            {t("ports.dontOpen")}
          </button>
        )}
      </div>
    )
  }

  if (state !== "closed") {
    return null
  }

  if (!able || (owner === null && onStage === undefined)) {
    return <div className="mt-1 text-xs text-alarm">{manual}</div>
  }

  return (
    <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-alarm">
      <span>{t("ports.closed", { port })}</span>
      {owner !== null ? (
        <button type="button" disabled={open.isPending} onClick={() => open.mutate(owner)} className={button}>
          {open.isPending ? t("ports.opening") : t("ports.open")}
        </button>
      ) : (
        <button type="button" onClick={() => onStage?.(true)} className={button}>
          {t("ports.openOnSave")}
        </button>
      )}
      {open.isError && <span title={said(open.error)}>{t(complaint(open.error))}</span>}
    </div>
  )
}

function said(error: unknown): string | undefined {
  if (!axios.isAxiosError(error)) {
    return undefined
  }

  return (error.response?.data as { message?: string } | undefined)?.message
}

// Asks whether the ports the panel held open before a port moved are closed as the form is saved.
export function KeepQuestion({
  ports,
  onAnswer,
  onCancel,
}: {
  ports: string[]
  onAnswer: (keep: boolean) => void
  onCancel: () => void
}) {
  const t = useText()

  return (
    <Dialog
      title={t(ports.length === 1 ? "ports.keepOne" : "ports.keepMany", { list: ports.join(", ") })}
      onClose={onCancel}
      actions={
        <>
          <button type="button" className={secondary} onClick={onCancel}>
            {t("ports.cancel")}
          </button>
          <button type="button" className={secondary} title={t("ports.keepHint")} onClick={() => onAnswer(true)}>
            {t("ports.keep")}
          </button>
          <button type="button" className={primary} onClick={() => onAnswer(false)}>
            {t("ports.close")}
          </button>
        </>
      }
    />
  )
}
