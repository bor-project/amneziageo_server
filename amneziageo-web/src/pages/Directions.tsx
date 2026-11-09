import { kindKey } from "@/components/routeKind"
import { useRoutingEditor } from "@/components/useRoutingEditor"
import { complaint } from "@/api/auth"
import { useFailure } from "@/api/failure"
import { useMoveOutbound, useOutbounds } from "@/api/outbounds"
import type { Outbound } from "@/api/outbounds"
import { useRules } from "@/api/rules"
import { scopes } from "@/api/scopes"
import { Find } from "@/components/fields"
import { Move } from "@/components/Move"
import { RoutingSettings, RoutingToolbar } from "@/components/RoutingEditor"
import { Rows } from "@/components/Rows"
import { alarmLine, card } from "@/components/styles"
import { bytes } from "@/format"
import { useLanguage, useText } from "@/i18n"
import { ChannelState } from "@/pages/Channels"
import { RoutingOutboundEditor } from "@/pages/RoutingWayEditor"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

interface Direction {
  key: string
  name: string
  outbound: Outbound | null
  action: "out" | "direct" | "block"
}

export function Directions() {
  const t = useText()
  const language = useLanguage()
  const user = useAppSelector((state) => state.auth.user)
  const editor = useRoutingEditor()
  const outbounds = useOutbounds()
  const failed = useFailure(outbounds)
  const rules = useRules().data ?? []
  const move = useMoveOutbound()
  const all = outbounds.data ?? []
  const may = holds(user, scopes.manageRouting)
  const find = (editor.params.get("find") ?? "").toLowerCase()
  const lines: Direction[] = [
    { key: "direct", name: t("rules.actionDirect"), outbound: null, action: "direct" },
    { key: "block", name: t("rules.actionBlock"), outbound: null, action: "block" },
    ...all.map((outbound): Direction => ({ key: String(outbound.id), name: outbound.name, outbound, action: "out" })),
  ]
  const shown = lines.filter((line) => (line.name + " " + (line.outbound?.host ?? "")).toLowerCase().includes(find))
  const selected = may && shown.some((line) => line.outbound?.id === editor.id) ? editor.id : null
  const open = (id: number | null) => <RoutingOutboundEditor key={id ?? "new"} id={id} onClose={editor.close} />

  return (
    <>
      <RoutingToolbar onAdd={may && outbounds.isSuccess ? () => editor.open("new") : undefined}>
        <Find value={editor.params.get("find") ?? ""} onChange={(value) => editor.filter("find", value)} className="w-full wide:w-72" />
      </RoutingToolbar>
      <div className={card}>
        {failed !== null && <div className={alarmLine}>{t(complaint(failed))}</div>}
        {outbounds.isPending && <div className="p-4 text-muted">{t("outbounds.loading")}</div>}
        <Rows name="routing-directions" items={shown} keyOf={(line) => line.key}
          creating={may && editor.fresh ? open(null) : undefined}
          expanded={selected === null ? undefined : { key: String(selected), body: open(selected) }}
          columns={[
            {
              key: "name", caption: t("routing.direction"), width: 200, lead: true,
              cell: (line) => <div>
                {line.outbound && may
                  ? <button type="button" className="font-semibold text-ink hover:text-brand-ink" onClick={() => editor.open(line.outbound!.id)}>{line.name}</button>
                  : <span className="font-semibold text-ink">{line.name}</span>}
                {line.outbound && <div className="mt-1 text-xs"><ChannelState outbound={line.outbound} t={t} language={language} /></div>}
              </div>,
            },
            { key: "kind", caption: t("outbounds.kind"), width: 150,
              cell: (line) => line.outbound ? t(kindKey(line.outbound.kind)) : t(line.action === "block" ? "routing.blockKind" : "routing.directKind") },
            {
              key: "address", caption: t("outbounds.host"),
              cell: (line) => <div>
                {line.outbound ? line.outbound.host + (line.outbound.port ? ":" + line.outbound.port : "") : line.action === "direct" ? t("routing.serverInternet") : "—"}
                {line.outbound?.state?.rxBytes != null && line.outbound.state.txBytes != null && <div className="mt-1 text-xs text-muted">
                  {bytes(t, line.outbound.state.rxBytes) + " / " + bytes(t, line.outbound.state.txBytes)}
                </div>}
              </div>,
            },
            { key: "rules", width: 80, caption: t("tab.rules"), cell: (line) => rules.filter((rule) => rule.action === line.action && (line.action !== "out" || rule.outbound === line.name)).length },
            {
              key: "actions", width: 120, caption: t("outbounds.actions"), tail: true,
              cell: (line) => line.outbound && may && <div className="flex justify-end gap-1">
                <Move first={all[0]?.id === line.outbound.id || move.isPending}
                  last={all.at(-1)?.id === line.outbound.id || move.isPending}
                  upTitle={t("outbounds.up")} downTitle={t("outbounds.down")}
                  onMove={(up) => void move.mutateAsync({ id: line.outbound!.id, up }).catch(() => undefined)} />
                <RoutingSettings onClick={() => editor.open(line.outbound!.id)} open={editor.id === line.outbound.id} />
              </div>,
            },
          ]}
        />
      </div>
      {move.error && <div className="mt-2 text-sm text-alarm">{t(complaint(move.error))}</div>}
    </>
  )
}
