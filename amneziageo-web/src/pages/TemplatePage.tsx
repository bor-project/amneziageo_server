import { Navigate, useNavigate, useParams } from "react-router-dom"
import { draftOf, freshTemplate, useAddTemplate, useChangeTemplate, useTemplates } from "@/api/templates"
import { TemplateForm } from "@/components/TemplateForm"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { templateTrail } from "@/pages/trails"

export function TemplatePage() {
  const t = useText()
  const navigate = useNavigate()
  const { templateId } = useParams()
  const templates = useTemplates()
  const add = useAddTemplate()
  const change = useChangeTemplate()
  const held = templateId === undefined ? undefined : templates.data?.find((one) => one.id === Number(templateId))

  useTail(
    held === undefined
      ? [{ label: t("templates.newTitle") }]
      : [templateTrail(held, templates.data ?? []), { label: t("templates.edit") }],
  )

  if (templateId === undefined) {
    return (
      <TemplateForm
        start={freshTemplate}
        pending={add.isPending}
        error={add.error}
        onSave={(draft) => void add.mutateAsync(draft).then((made) => navigate(`/connections/templates/${made.id}`))}
        onClose={() => navigate("/connections/templates")}
      />
    )
  }

  if (held === undefined) {
    return templates.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("templates.loading")}</div>
    ) : (
      <Navigate to="/connections/templates" replace />
    )
  }

  const card = `/connections/templates/${held.id}`

  return (
    <TemplateForm
      start={draftOf(held)}
      held={held}
      pending={change.isPending}
      error={change.error}
      onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(card))}
      onClose={() => navigate(card)}
    />
  )
}
