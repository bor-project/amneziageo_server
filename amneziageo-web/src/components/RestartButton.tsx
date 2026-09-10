import { useResolver, useRestartResolver } from "@/api/dns"
import { usePanel, useRestartPanel } from "@/api/panel"
import { scopes } from "@/api/scopes"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function RestartButton() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const panel = usePanel().data
  const resolver = useResolver(false).data
  const panelRestart = useRestartPanel()
  const resolverRestart = useRestartResolver()
  const panelDue = holds(user, scopes.manageAccess) && panel?.pending === true
  const resolverDue = holds(user, scopes.manageRouting) && resolver?.pending === true
  const busy = panelRestart.isPending || resolverRestart.isPending

  if (!panelDue && !resolverDue && !busy) {
    return null
  }

  function restart() {
    if (panelDue) {
      panelRestart.mutate()
      return
    }

    resolverRestart.mutate()
  }

  return (
    <button
      type="button"
      disabled={busy}
      onClick={restart}
      className="flex items-center gap-1.5 rounded border border-warn bg-warn-soft px-2.5 py-1 text-sm font-medium text-warn disabled:opacity-50"
    >
      <Exclamation />
      {t("layout.restart")}
    </button>
  )
}

function Exclamation() {
  return (
    <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M12 3.5 2.5 20h19z" strokeLinejoin="round" />
      <path d="M12 10v4.5M12 17.5v.01" strokeLinecap="round" />
    </svg>
  )
}
