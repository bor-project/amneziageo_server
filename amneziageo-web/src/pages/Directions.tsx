import { kindKey } from "@/components/routeKind"
import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useFailure } from "@/api/failure"
import { useMoveOutbound, useOutbounds } from "@/api/outbounds"
import { useRoutingEntries } from "@/api/routingEntries"
import { scopes } from "@/api/scopes"
import { Find } from "@/components/fields"
import { Move } from "@/components/Move"
import { RoutingSettings, RoutingToolbar } from "@/components/RoutingEditor"
import { Rows } from "@/components/Rows"
import { alarmLine, card } from "@/components/styles"
import { bytes } from "@/format"
import { useLanguage, useText } from "@/i18n"
import { ChannelState } from "@/components/RouteStates"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Directions() {
  const t = useText()
  const language = useLanguage()
  const user = useAppSelector((state) => state.auth.user)
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const outbounds = useOutbounds()
  const failed = useFailure(outbounds)
  const rules = useRoutingEntries().data ?? []
  const move = useMoveOutbound()
  const all = (outbounds.data ?? []).filter((one) => one.kind !== "local")
  const may = holds(user, scopes.manageRouting)
  const find = params.get("find") ?? ""
  const shown = all.filter((one) => (one.name + " " + one.host).toLowerCase().includes(find.trim().toLowerCase()))

  function filter(value: string) {
    const next = new URLSearchParams(params)
    next.delete("edit")
    if (value === "") {
      next.delete("find")
    } else {
      next.set("find", value)
    }
    setParams(next, { replace: true })
  }

  return (
    <>
      <RoutingToolbar onAdd={may && outbounds.isSuccess ? () => navigate("/routing/channels/new") : undefined}>
        <Find value={find} onChange={filter} className="w-full wide:w-72" />
      </RoutingToolbar>
      <div className={card}>
        {failed !== null && <div className={alarmLine}>{t(complaint(failed))}</div>}
        {outbounds.isPending && <div className="p-4 text-muted">{t("outbounds.loading")}</div>}
        <Rows name="routing-linked-servers" items={shown} keyOf={(one) => one.id}
          columns={[
            {
              key: "name", caption: t("routing.linkedServer"), width: 200, lead: true,
              cell: (one) => <div>
                {may
                  ? <Link to={`/routing/channels/${one.id}/edit`} className="font-semibold text-ink hover:text-brand-ink">{one.name}</Link>
                  : <span className="font-semibold text-ink">{one.name}</span>}
                <div className="mt-1 text-xs"><ChannelState outbound={one} t={t} language={language} /></div>
              </div>,
            },
            { key: "kind", caption: t("outbounds.kind"), width: 150,
              cell: (one) => t(kindKey(one.kind)) },
            {
              key: "address", caption: t("outbounds.host"),
              cell: (one) => <div>
                {one.kind === "local" ? t("outbounds.kindLocal") : one.host + (one.port ? ":" + one.port : "")}
                <div className="mt-1 text-xs text-muted">DNS: {one.dns?.join(", ") || "—"}</div>
                {one.state?.rxBytes != null && one.state.txBytes != null && <div className="mt-1 text-xs text-muted">
                  {bytes(t, one.state.rxBytes) + " / " + bytes(t, one.state.txBytes)}
                </div>}
              </div>,
            },
            { key: "rules", width: 80, caption: t("tab.rules"), cell: (one) => rules.filter((rule) => rule.action === "out" && rule.outbound === one.name).length },
            {
              key: "actions", width: 120, caption: t("outbounds.actions"), tail: true,
              cell: (one) => may && <div className="flex justify-end gap-1">
                <Move first={all[0]?.id === one.id || move.isPending}
                  last={all.at(-1)?.id === one.id || move.isPending}
                  upTitle={t("outbounds.up")} downTitle={t("outbounds.down")}
                  onMove={(up) => void move.mutateAsync({ id: one.id, up }).catch(() => undefined)} />
                <RoutingSettings onClick={() => navigate(`/routing/channels/${one.id}/edit`)} />
              </div>,
            },
          ]}
        />
      </div>
      {move.error && <div className="mt-2 text-sm text-alarm">{t(complaint(move.error))}</div>}
    </>
  )
}
