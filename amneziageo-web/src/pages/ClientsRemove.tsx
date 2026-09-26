import { useState } from "react"
import { Link, Navigate, useNavigate, useSearchParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useClients, useRemoveClients } from "@/api/clients"
import { summary } from "@/components/batch"
import type { Summary } from "@/components/batch"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function ClientsRemove() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/connections/clients")
  const [params] = useSearchParams()
  const clients = useClients()
  const remove = useRemoveClients()
  const [fault, setFault] = useState<Summary | null>(null)
  const all = clients.data ?? []
  const wanted = new Set((params.get("ids") ?? "").split(",").map(Number))
  const held = all.filter((one) => wanted.has(one.id))

  useTail([{ label: t("clients.removeMany") }])

  if (held.length === 0) {
    return clients.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("clients.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  function nameOf(id: number): string {
    return all.find((one) => one.id === id)?.name ?? `#${id}`
  }

  async function go() {
    const ids = held.map((one) => one.id)
    setFault(null)
    try {
      const answer = await remove.mutateAsync(ids)
      const told = summary(t, answer, ids.length, "clients.removed", nameOf)
      if (told.text.length === 0) {
        navigate(back)
      } else {
        setFault(told)
      }
    } catch (error) {
      setFault({ text: t(complaint(error)), title: "" })
    }
  }

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("clients.removeManyTitle", { count: held.length })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        {held.map((one) => (
          <div key={one.id} className="mt-1.5 text-[13px]">
            <span className="text-ink">{one.name}</span>
            <span className="ml-2 text-muted">{one.address.join(", ")}</span>
          </div>
        ))}
      </div>

      {fault !== null && (
        <div className="text-sm text-alarm" title={fault.title || undefined}>
          {fault.text}
        </div>
      )}

      <div className="flex justify-end gap-2">
        <Link to={back} className={`flex h-10 items-center ${secondary}`}>
          {t("action.backToList")}
        </Link>
        <button type="button" onClick={() => void go()} disabled={remove.isPending} className={danger}>
          {t("clients.remove")}
        </button>
      </div>
    </div>
  )
}
