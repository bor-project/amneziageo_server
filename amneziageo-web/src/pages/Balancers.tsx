import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useBalancers } from "@/api/balancers"
import { useFailure } from "@/api/failure"
import { scopes } from "@/api/scopes"
import { Find } from "@/components/fields"
import { RoutingSettings, RoutingToolbar } from "@/components/RoutingEditor"
import { Rows } from "@/components/Rows"
import { alarmLine, card } from "@/components/styles"
import { useText } from "@/i18n"
import { GroupState, Members } from "@/components/RouteStates"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Balancers() {
  const t = useText()
  const user = useAppSelector((state) => state.auth.user)
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const groups = useBalancers()
  const failed = useFailure(groups)
  const may = holds(user, scopes.manageRouting)
  const find = params.get("find") ?? ""
  const all = groups.data ?? []
  const shown = all.filter((group) => (group.name + " " + group.members.join(" ")).toLowerCase().includes(find.trim().toLowerCase()))
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
      <RoutingToolbar onAdd={may && groups.isSuccess ? () => navigate("/routing/balancers/new") : undefined}>
        <Find value={find} onChange={filter} className="w-full wide:w-72" />
      </RoutingToolbar>
      <div className={card}>
        {failed !== null && <div className={alarmLine}>{t(complaint(failed))}</div>}
        {groups.isPending && <div className="p-4 text-muted">{t("outbounds.loading")}</div>}
        {groups.data !== undefined && <Rows name="routing-balancers" items={shown} keyOf={(group) => group.id}
          columns={[
            { key: "name", caption: t("routing.balancer"), width: 220, lead: true,
              cell: (group) => <div>
                {may ? <Link to={`/routing/balancers/${group.id}/edit`} className="font-semibold text-ink hover:text-brand-ink">{group.name}</Link> : group.name}
                <div className="mt-1 text-xs"><GroupState balancer={group} t={t} /></div>
              </div> },
            { key: "members", caption: t("routing.linkedServers"), cell: (group) => <Members members={group.state.members} /> },
            { key: "strategy", caption: t("balancers.strategy"), width: 160,
              cell: (group) => t(group.strategy === "round" ? "balancers.round" : group.strategy === "sticky" ? "balancers.sticky" : "balancers.priority") },
            { key: "actions", caption: t("balancers.actions"), width: 56, tail: true,
              cell: (group) => may && <div className="flex justify-end"><RoutingSettings onClick={() => navigate(`/routing/balancers/${group.id}/edit`)} /></div> },
          ]}
        />}
      </div>
    </>
  )
}
