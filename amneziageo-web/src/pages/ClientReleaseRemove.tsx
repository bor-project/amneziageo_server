import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { shownTag, useClientRelease, useRemoveClientRelease } from "@/api/clientUpdates"
import { useTail } from "@/components/crumbs"
import { card, danger, secondary } from "@/components/styles"
import { bytes } from "@/format"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function ClientReleaseRemove() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/settings/client-updates")
  const { tag } = useParams()
  const release = useClientRelease(tag)
  const remove = useRemoveClientRelease()
  const held = release.data

  useTail(
    held === undefined
      ? []
      : [
          { label: shownTag(held.release.tag), to: `/settings/client-updates/${held.release.tag}/edit` },
          { label: t("releases.remove") },
        ],
  )

  if (held === undefined) {
    return release.isPending ? (
      <div className="mt-4 text-sm text-muted">{t("releases.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("releases.removeTitle", { name: shownTag(held.release.tag) })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        <div className="mt-1 text-[13px] text-muted">
          {t("releases.removeFiles", { count: held.files.length, size: bytes(t, held.release.bytes) })}
        </div>
      </div>

      {remove.error !== null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}

      <div className="flex justify-end gap-2">
        <Link to={back} className={`flex h-10 items-center ${secondary}`}>
          {t("action.backToList")}
        </Link>
        <button
          type="button"
          onClick={() => void remove.mutateAsync(held.release.tag).then(() => navigate(back))}
          disabled={remove.isPending}
          className={danger}
        >
          {t("releases.remove")}
        </button>
      </div>
    </div>
  )
}
