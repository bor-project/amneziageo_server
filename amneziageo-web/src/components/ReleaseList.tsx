import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { channelWords, releasePage, releasesPerPage, shownTag, useClientReleases } from "@/api/clientUpdates"
import type { ReleaseSummary } from "@/api/clientUpdates"
import { useFailure } from "@/api/failure"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { alarmBar, alarmLine, card, quiet, tool } from "@/components/styles"
import { bytes } from "@/format"
import { useLanguage, useText } from "@/i18n"

export function ReleaseList({ may }: { may: boolean }) {
  const t = useText()
  const language = useLanguage()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number(params.get("page") ?? "1") || 1)
  const releases = useClientReleases(page)
  const failed = useFailure(releases)
  const items = releases.data?.items ?? []
  const pages = Math.max(1, Math.ceil((releases.data?.total ?? 0) / releasesPerPage))

  function turn(to: number) {
    const kept = new URLSearchParams(params)
    if (to === 1) {
      kept.delete("page")
    } else {
      kept.set("page", String(to))
    }

    setParams(kept, { replace: true })
  }

  return (
    <div className={card}>
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-line px-4 py-3">
        <div className="text-sm font-semibold text-ink-soft">{t("releases.title")}</div>
        {may && (
          <div className="flex flex-wrap gap-2">
            <Link to={releasePage("new")} className={tool}>
              {t("releases.create")}
            </Link>
            <Link to={releasePage("new?archive=1")} className={tool}>
              {t("releases.archive")}
            </Link>
          </div>
        )}
      </div>

      {releases.isPending && failed === null && <div className="px-4 py-6 text-sm text-muted">{t("releases.loading")}</div>}
      {failed !== null && <div className={items.length === 0 ? alarmLine : alarmBar}>{t(complaint(failed))}</div>}
      {releases.isSuccess && items.length === 0 && (
        <div className="px-4 py-6 text-sm text-muted">{t("releases.empty")}</div>
      )}

      {items.length > 0 && (
        <Rows
          name="client-releases"
          items={items}
          keyOf={(one) => one.tag}
          columns={[
            {
              key: "version",
              width: 200,
              caption: t("releases.version"),
              lead: true,
              body: "font-semibold text-ink",
              cell: (one) =>
                may ? (
                  <Link to={editOf(one)} className="truncate hover:text-brand-ink">
                    {shownTag(one.tag)}
                  </Link>
                ) : (
                  <span className="truncate">{shownTag(one.tag)}</span>
                ),
            },
            {
              key: "channel",
              width: 130,
              caption: t("releases.channel"),
              cell: (one) => t(channelWords[one.channel] ?? "channel.stable"),
            },
            {
              key: "date",
              width: 130,
              caption: t("releases.date"),
              cell: (one) => new Date(one.created).toLocaleDateString(language),
            },
            { key: "files", width: 90, caption: t("releases.files"), cell: (one) => `${one.files}/20` },
            { key: "size", caption: t("releases.size"), cell: (one) => bytes(t, one.bytes) },
            {
              key: "actions",
              caption: "",
              tail: true,
              width: 56,
              cell: (one) =>
                may && (
                  <RowActions
                    title={t("releases.actions")}
                    actions={[
                      { label: t("releases.edit"), onPick: () => navigate(editOf(one)) },
                      {
                        label: t("releases.remove"),
                        alarming: true,
                        onPick: () => navigate(releasePage(`${one.tag}/delete`)),
                      },
                    ]}
                  />
                ),
            },
          ]}
        />
      )}

      {pages > 1 && (
        <div className="flex items-center justify-end gap-1 border-t border-line px-4 py-2.5">
          <button
            type="button"
            className={quiet}
            disabled={page <= 1}
            aria-label={t("releases.previous")}
            title={t("releases.previous")}
            onClick={() => turn(page - 1)}
          >
            &lsaquo;
          </button>
          {Array.from({ length: pages }, (_, at) => at + 1).map((one) => (
            <button
              key={one}
              type="button"
              aria-current={one === page ? "page" : undefined}
              className={`${quiet} ${one === page ? "bg-active text-ink" : ""}`}
              onClick={() => turn(one)}
            >
              {one}
            </button>
          ))}
          <button
            type="button"
            className={quiet}
            disabled={page >= pages}
            aria-label={t("releases.next")}
            title={t("releases.next")}
            onClick={() => turn(page + 1)}
          >
            &rsaquo;
          </button>
        </div>
      )}
    </div>
  )
}

function editOf(one: ReleaseSummary): string {
  return releasePage(`${one.tag}/edit`)
}
