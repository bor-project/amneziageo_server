import { Navigate, useNavigate, useParams } from "react-router-dom"
import { draftOf, freshTemplate, useAddTemplate, useChangeTemplate, useTemplates } from "@/api/templates"
import { Crumbs } from "@/components/Crumbs"
import { TemplateForm } from "@/components/TemplateForm"
import { useText } from "@/i18n"

export function TemplatePage() {
  const t = useText()
  const navigate = useNavigate()
  const { templateId } = useParams()
  const templates = useTemplates()
  const add = useAddTemplate()
  const change = useChangeTemplate()
  const held = templateId === undefined ? undefined : templates.data?.find((one) => one.id === Number(templateId))

  function back() {
    navigate("/connections/templates")
  }

  function trail(title: string) {
    return <Crumbs items={[{ to: "/connections/templates", label: t("tab.templates") }, { label: title }]} />
  }

  if (templateId === undefined) {
    return (
      <div>
        {trail(t("templates.newTitle"))}
        <TemplateForm
          start={freshTemplate}
          pending={add.isPending}
          error={add.error}
          onSave={(draft) => void add.mutateAsync(draft).then(back)}
          onClose={back}
        />
      </div>
    )
  }

  if (held === undefined) {
    return templates.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("templates.loading")}</div>
    ) : (
      <Navigate to="/connections/templates" replace />
    )
  }

  return (
    <div>
      {trail(held.name)}
      <TemplateForm
        start={draftOf(held)}
        held={held}
        pending={change.isPending}
        error={change.error}
        onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(back)}
        onClose={back}
      />
    </div>
  )
}
