import { Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import {
  draftOf,
  freshTemplate,
  useAddTemplate,
  useChangeTemplate,
  useRefreshTemplate,
  useTemplates,
} from "@/api/templates"
import { TemplateForm } from "@/components/TemplateForm"
import { useTail } from "@/components/crumbs"
import { secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function TemplatePage() {
  const t = useText()
  const navigate = useNavigate()
  const { templateId } = useParams()
  const templates = useTemplates()
  const add = useAddTemplate()
  const change = useChangeTemplate()
  const refresh = useRefreshTemplate()
  const held = templateId === undefined ? undefined : templates.data?.find((one) => one.id === Number(templateId))
  const back = useSpot("/connections/templates")

  useTail(held === undefined ? [{ label: t("templates.newTitle") }] : [{ label: held.name }])

  if (templateId === undefined) {
    return (
      <TemplateForm
        start={freshTemplate}
        pending={add.isPending}
        error={add.error}
        onSave={(draft) => void add.mutateAsync(draft).then(() => navigate(back))}
        onClose={() => navigate(back)}
      />
    )
  }

  if (held === undefined) {
    return templates.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("templates.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  return (
    <div>
      <div className="mt-4 flex justify-end gap-2">
        <button
          type="button"
          onClick={() =>
            void add
              .mutateAsync({ ...draftOf(held), name: t("templates.copyName", { name: held.name }) })
              .then((made) => navigate(`/connections/templates/${made.id}/edit`))
          }
          disabled={add.isPending}
          className={secondary}
        >
          {t("templates.duplicate")}
        </button>
        {held.entries.length > 0 && (
          <button
            type="button"
            onClick={() => refresh.mutate(held.id)}
            disabled={refresh.isPending}
            className={secondary}
          >
            {refresh.isPending ? t("templates.refreshing") : t("templates.refresh")}
          </button>
        )}
      </div>

      {refresh.error !== null && <div className="mt-2 text-sm text-alarm">{t(complaint(refresh.error))}</div>}
      {add.error !== null && <div className="mt-2 text-sm text-alarm">{t(complaint(add.error))}</div>}

      <TemplateForm
        start={draftOf(held)}
        held={held}
        pending={change.isPending}
        error={change.error}
        onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(back))}
        onClose={() => navigate(back)}
        onRemove={() => navigate(`/connections/templates/${held.id}/delete`)}
      />
    </div>
  )
}
