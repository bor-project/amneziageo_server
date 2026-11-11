import { useNavigate } from "react-router-dom"
import { draftOf as balanceDraft, freshBalancer, useAddBalancer, useBalancers, useChangeBalancer } from "@/api/balancers"
import { draftOf, useAddOutbound, useApplyOutbound, useChangeOutbound, useFreshOutbound, useOutbounds, useProbeOutbound } from "@/api/outbounds"
import { BalancerForm } from "@/components/BalancerForm"
import { OutboundForm } from "@/components/OutboundForm"
import { RoutingEditor } from "@/components/RoutingEditor"
import { secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function RoutingBalancerEditor({ id, onClose }: { id: number | null; onClose: () => void }) {
  const t = useText()
  const navigate = useNavigate()
  const groups = useBalancers()
  const add = useAddBalancer()
  const change = useChangeBalancer()
  const held = groups.data?.find((group) => group.id === id)

  if (id !== null && held === undefined) {
    return <div className="p-4 text-sm text-muted">{t("outbounds.loading")}</div>
  }

  return (
    <RoutingEditor title={t("routing.balancer")} onClose={onClose}>
      <BalancerForm embedded start={held ? balanceDraft(held) : freshBalancer} self={held?.name}
        pending={add.isPending || change.isPending} error={add.error ?? change.error}
        onSave={(draft) => void (held ? change.mutateAsync({ id: held.id, draft }) : add.mutateAsync(draft))
          .then(onClose).catch(() => undefined)}
        onClose={onClose}
        onRemove={held ? () => navigate("/routing/channels/groups/" + held.id + "/delete") : undefined}
      />
    </RoutingEditor>
  )
}

export function RoutingOutboundEditor({ id, onClose }: { id: number | null; onClose: () => void }) {
  const t = useText()
  const navigate = useNavigate()
  const outbounds = useOutbounds()
  const held = outbounds.data?.find((way) => way.id === id)
  const names = new Set((outbounds.data ?? []).map((way) => way.name))
  let index = 1
  while (names.has("out" + index)) {
    index++
  }

  const fresh = useFreshOutbound(id === null && outbounds.data !== undefined, "out" + index, "wg")
  const add = useAddOutbound()
  const change = useChangeOutbound()
  const apply = useApplyOutbound()
  const probe = useProbeOutbound()
  const start = held ?? fresh.data

  if (start === undefined) {
    return <div className="p-4 text-sm text-muted">{t("outbounds.loading")}</div>
  }

  return (
    <RoutingEditor title={t("routing.direction")} onClose={onClose}>
      {held && <div className="mt-3 flex flex-wrap justify-end gap-2">
        <button type="button" className={secondary} disabled={probe.isPending}
          onClick={() => void probe.mutateAsync(held.id).catch(() => undefined)}>{t("outbounds.probeNow")}</button>
        <button type="button" className={secondary} disabled={apply.isPending}
          onClick={() => void apply.mutateAsync(held.id).catch(() => undefined)}>{t("outbounds.apply")}</button>
      </div>}
      <OutboundForm embedded start={draftOf(start)} publicKey={start.publicKey} self={held?.name}
        pending={add.isPending || change.isPending} error={add.error ?? change.error}
        onSave={(draft) => void (held ? change.mutateAsync({ id: held.id, draft }) : add.mutateAsync(draft))
          .then(onClose).catch(() => undefined)}
        onClose={onClose}
        onRemove={held ? () => navigate("/routing/channels/" + held.id + "/delete") : undefined}
      />
    </RoutingEditor>
  )
}
