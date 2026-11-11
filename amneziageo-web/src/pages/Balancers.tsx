import { useRoutingEditor } from "@/components/useRoutingEditor"
import { complaint } from "@/api/auth"
import { useBalancers } from "@/api/balancers"
import { useFailure } from "@/api/failure"
import { scopes } from "@/api/scopes"
import { Find } from "@/components/fields"
import { RoutingSettings, RoutingToolbar } from "@/components/RoutingEditor"
import { Rows } from "@/components/Rows"
import { alarmLine, card } from "@/components/styles"
import { useText } from "@/i18n"
import { GroupState, Members } from "@/pages/Channels"
import { RoutingBalancerEditor } from "@/pages/RoutingWayEditor"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Balancers() {
  const t = useText()
  const user = useAppSelector((state) => state.auth.user)
  const editor = useRoutingEditor()
  const groups = useBalancers()
  const failed = useFailure(groups)
  const may = holds(user, scopes.manageRouting)
  const find = (editor.params.get("find") ?? "").toLowerCase()
  const all = groups.data ?? []
  const shown = all.filter((group) => (group.name + " " + group.members.join(" ")).toLowerCase().includes(find))
  const selected = may && shown.some((group) => group.id === editor.id) ? editor.id : null
  const open = (id: number | null) => <RoutingBalancerEditor key={id ?? "new"} id={id} onClose={editor.close} />

  return (
    <>
      <RoutingToolbar onAdd={may && groups.isSuccess ? () => editor.open("new") : undefined}>
        <Find value={editor.params.get("find") ?? ""} onChange={(value) => editor.filter("find", value)} className="w-full wide:w-72" />
      </RoutingToolbar>
      <div className={card}>
        {failed !== null && <div className={alarmLine}>{t(complaint(failed))}</div>}
        {groups.isPending && <div className="p-4 text-muted">{t("outbounds.loading")}</div>}
        {groups.data !== undefined && <Rows name="routing-balancers" items={shown} keyOf={(group) => group.id}
          creating={may && editor.fresh ? open(null) : undefined}
          expanded={selected === null ? undefined : { key: selected, body: open(selected) }}
          columns={[
            { key: "name", caption: t("routing.balancer"), width: 220, lead: true,
              cell: (group) => <div>
                {may ? <button type="button" className="font-semibold text-ink hover:text-brand-ink" onClick={() => editor.open(group.id)}>{group.name}</button> : group.name}
                <div className="mt-1 text-xs"><GroupState balancer={group} t={t} /></div>
              </div> },
            { key: "members", caption: t("routing.directions"), cell: (group) => <Members members={group.state.members} /> },
            { key: "strategy", caption: t("balancers.strategy"), width: 160,
              cell: (group) => t(group.strategy === "round" ? "balancers.round" : group.strategy === "sticky" ? "balancers.sticky" : "balancers.priority") },
            { key: "actions", caption: t("balancers.actions"), width: 56, tail: true,
              cell: (group) => may && <div className="flex justify-end"><RoutingSettings onClick={() => editor.open(group.id)} open={editor.id === group.id} /></div> },
          ]}
        />}
      </div>
    </>
  )
}
