import { Navigate, useNavigate, useParams } from "react-router-dom"
import { draftOf, useAddOutbound, useChangeOutbound, useFreshOutbound, useOutbounds } from "@/api/outbounds"
import type { Outbound } from "@/api/outbounds"
import { OutboundForm } from "@/components/OutboundForm"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { outboundTrail } from "@/pages/trails"

export function OutboundPage() {
  const { outboundId } = useParams()

  return outboundId === undefined ? <NewOutbound /> : <HeldOutbound outboundId={Number(outboundId)} />
}

function NewOutbound() {
  const t = useText()
  const navigate = useNavigate()
  const outbounds = useOutbounds()
  const fresh = useFreshOutbound(outbounds.data !== undefined, nextName(outbounds.data), "wg")
  const add = useAddOutbound()

  useTail([{ label: t("outbounds.newTitle") }])

  if (fresh.data === undefined) {
    return <div className="mt-4 text-sm text-muted">{t("outbounds.loading")}</div>
  }

  return (
    <OutboundForm
      start={draftOf(fresh.data)}
      publicKey={fresh.data.publicKey}
      pending={add.isPending}
      error={add.error}
      onSave={(draft) => void add.mutateAsync(draft).then(() => navigate("/routing/channels"))}
      onClose={() => navigate("/routing/channels")}
    />
  )
}

function HeldOutbound({ outboundId }: { outboundId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const outbounds = useOutbounds()
  const change = useChangeOutbound()
  const all = outbounds.data ?? []
  const held = all.find((one) => one.id === outboundId)

  useTail(held === undefined ? [] : [outboundTrail(held, all), { label: t("outbounds.edit") }])

  if (held === undefined) {
    return outbounds.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("outbounds.loading")}</div>
    ) : (
      <Navigate to="/routing/channels" replace />
    )
  }

  return (
    <OutboundForm
      start={draftOf(held)}
      publicKey={held.publicKey}
      pending={change.isPending}
      error={change.error}
      onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate("/routing/channels"))}
      onClose={() => navigate("/routing/channels")}
    />
  )
}

function nextName(outbounds: Outbound[] | undefined): string {
  const taken = new Set((outbounds ?? []).map((one) => one.name))
  let at = 1
  while (taken.has(`out${at}`)) {
    at++
  }

  return `out${at}`
}
