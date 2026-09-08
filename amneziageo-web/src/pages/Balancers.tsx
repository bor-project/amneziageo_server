import { useState } from "react"
import {
  draftOf,
  freshBalancer,
  useAddBalancer,
  useBalancers,
  useChangeBalancer,
  useRemoveBalancer,
  useSwitchBalancer,
} from "@/api/balancers"
import type { Balancer, BalancerMember } from "@/api/balancers"
import { useOutbounds } from "@/api/outbounds"
import { scopes } from "@/api/scopes"
import { BalancerForm } from "@/components/BalancerForm"
import { Modal } from "@/components/Modal"
import { RowActions } from "@/components/RowActions"
import { card, danger, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Balancers() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const balancers = useBalancers()
  const outbounds = useOutbounds()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<Balancer | null>(null)
  const [removing, setRemoving] = useState<Balancer | null>(null)
  const add = useAddBalancer()
  const change = useChangeBalancer()
  const remove = useRemoveBalancer()
  const turn = useSwitchBalancer()
  const may = holds(user, scopes.manageRouting)

  return (
    <div>
      <h1 className="text-xl font-semibold">{t("nav.balancers")}</h1>

      <div className={`mt-4 ${card}`}>
        {may && (
          <div className="flex justify-end border-b border-line px-4 py-3">
            <button
              type="button"
              onClick={() => setAdding(true)}
              disabled={(outbounds.data?.length ?? 0) === 0}
              className={primary}
            >
              {t("balancers.add")}
            </button>
          </div>
        )}

        {balancers.data?.length === 0 && (
          <div className="px-4 py-6 text-sm text-muted">{t("balancers.empty")}</div>
        )}

        {balancers.data && balancers.data.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-xs text-muted">
                <tr>
                  <th className="px-4 py-2 font-normal">{t("balancers.name")}</th>
                  <th className="px-4 py-2 font-normal">{t("balancers.strategy")}</th>
                  <th className="px-4 py-2 font-normal">{t("balancers.members")}</th>
                  <th className="px-4 py-2 font-normal">{t("balancers.state")}</th>
                  <th className="px-4 py-2" />
                </tr>
              </thead>
              <tbody>
                {balancers.data.map((balancer) => (
                  <tr key={balancer.id} className="border-t border-line">
                    <td className="px-4 py-2 font-medium text-ink">
                      {balancer.name}
                      {!balancer.isEnabled && <span className="ml-2 text-xs text-muted">{t("balancers.off")}</span>}
                    </td>
                    <td className="px-4 py-2 text-muted">{t(strategy(balancer))}</td>
                    <td className="px-4 py-2">
                      <Members members={balancer.state.members} />
                    </td>
                    <td className="px-4 py-2">
                      <State balancer={balancer} t={t} />
                    </td>
                    <td className="px-4 py-2">
                      {may && (
                        <RowActions
                          title={t("balancers.actions")}
                          actions={[
                            {
                              label: balancer.isEnabled ? t("balancers.turnOff") : t("balancers.turnOn"),
                              onPick: () => void turn.mutateAsync({ id: balancer.id, on: !balancer.isEnabled }),
                            },
                            { label: t("balancers.edit"), onPick: () => setEditing(balancer) },
                            { label: t("balancers.remove"), onPick: () => setRemoving(balancer), alarming: true },
                          ]}
                        />
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {adding && (
        <BalancerForm
          title={t("balancers.newTitle")}
          start={freshBalancer}
          pending={add.isPending}
          error={add.error}
          onSave={(draft) => void add.mutateAsync(draft).then(() => setAdding(false))}
          onClose={() => setAdding(false)}
        />
      )}

      {editing && (
        <BalancerForm
          title={t("balancers.editTitle", { name: editing.name })}
          start={draftOf(editing)}
          pending={change.isPending}
          error={change.error}
          onSave={(draft) => void change.mutateAsync({ id: editing.id, draft }).then(() => setEditing(null))}
          onClose={() => setEditing(null)}
        />
      )}

      {removing && (
        <Modal
          title={t("balancers.removeTitle", { name: removing.name })}
          onClose={() => setRemoving(null)}
          footer={
            <>
              <button type="button" onClick={() => setRemoving(null)} className={secondary}>
                {t("balancers.cancel")}
              </button>
              <button
                type="button"
                onClick={() => void remove.mutateAsync(removing.id).then(() => setRemoving(null))}
                disabled={remove.isPending}
                className={danger}
              >
                {t("balancers.remove")}
              </button>
            </>
          }
        >
          <div className="text-sm text-muted">{removing.members.join(", ")}</div>
        </Modal>
      )}
    </div>
  )
}

function Members({ members }: { members: BalancerMember[] }) {
  return (
    <span className="flex flex-wrap gap-2">
      {members.map((member) => (
        <span
          key={member.name}
          className={member.isAlive ? "rounded bg-brand-soft px-2 py-0.5 text-brand-ink" : "rounded px-2 py-0.5 text-muted"}
        >
          {member.name}
        </span>
      ))}
    </span>
  )
}

function State({ balancer, t }: { balancer: Balancer; t: Text }) {
  if (!balancer.isEnabled) {
    return <span className="text-muted">{t("balancers.off")}</span>
  }

  if (!balancer.state.isLive) {
    return <span className="text-alarm">{t("error.noLiveMember")}</span>
  }

  return <span className="text-ink">{t("balancers.live")}</span>
}

function strategy(balancer: Balancer) {
  if (balancer.strategy === "round") {
    return "balancers.round" as const
  }

  return balancer.strategy === "sticky" ? ("balancers.sticky" as const) : ("balancers.priority" as const)
}
