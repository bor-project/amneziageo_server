import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useRemoveEntry, useRoutingEntries } from "@/api/routingEntries"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function EntryRemove() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/routing/rules")
  const { entryId } = useParams()
  const entries = useRoutingEntries()
  const remove = useRemoveEntry()
  const held = entries.data?.find((one) => one.id === Number(entryId))
  useTail(held === undefined ? [] : [{ label: held.name, to: `/routing/rules/${held.id}/edit` }, { label: t("rules.remove") }])

  if (held === undefined) {
    return entries.data === undefined ? <div className="mt-4 text-sm text-muted">{t("rules.loading")}</div> : <Navigate to={back} replace />
  }

  return <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
    <h2 className="text-[22px] font-semibold">{t("rules.removeTitle", { name: held.name })}</h2>
    <div className={`border-alarm-line p-4 ${card}`}><div className="text-sm font-semibold text-alarm">{t("action.forever")}</div></div>
    {remove.error != null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}
    <div className="flex justify-end gap-2">
      <Link to={back} className={`flex h-10 items-center ${secondary}`}>{t("action.backToList")}</Link>
      <button type="button" className={danger} disabled={remove.isPending}
        onClick={() => void remove.mutateAsync(held.id).then(() => navigate(back)).catch(() => undefined)}>{t("rules.remove")}</button>
    </div>
  </div>
}
