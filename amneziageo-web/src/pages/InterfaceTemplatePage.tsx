import { useState } from "react"
import { Navigate, useNavigate, useParams } from "react-router-dom"
import {
  draftOf,
  useAddInterfaceTemplate,
  useChangeInterfaceTemplate,
  useFreshInterfaceTemplate,
  useInterfaceTemplates,
} from "@/api/interfaceTemplates"
import type { InterfaceTemplateSave } from "@/api/interfaceTemplates"
import { scopes } from "@/api/scopes"
import { InterfaceTemplateForm } from "@/components/InterfaceTemplateForm"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"
import { lastSpot } from "@/store/spots"

export function InterfaceTemplatePage() {
  const { templateId } = useParams()

  return templateId === undefined ? <NewTemplate /> : <HeldTemplate templateId={Number(templateId)} />
}

function NewTemplate() {
  const t = useText()
  const navigate = useNavigate()
  const fresh = useFreshInterfaceTemplate(true)
  const add = useAddInterfaceTemplate()
  const back = lastSpot("connections", "/connections/templates/interfaces")

  useTail([{ label: t("templates.newInterface") }])

  if (fresh.data === undefined) {
    return <div className="mt-4 text-sm text-muted">{t("templates.loading")}</div>
  }

  return (
    <InterfaceTemplateForm
      start={{ ...draftOf(fresh.data), name: "" }}
      pending={add.isPending}
      error={add.error}
      onSave={(draft) => void add.mutateAsync(draft).then(() => navigate(back))}
      onClose={() => navigate(back)}
    />
  )
}

function HeldTemplate({ templateId }: { templateId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const templates = useInterfaceTemplates()
  const change = useChangeInterfaceTemplate()
  const [left, setLeft] = useState<string[]>([])
  const may = holds(user, scopes.manageInterfaces)
  const held = templates.data?.find((one) => one.id === templateId)
  const back = lastSpot("connections", "/connections/templates/interfaces")

  useTail(held === undefined ? [] : [{ label: held.name }, { label: t("templates.edit") }])

  if (held === undefined) {
    return templates.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("templates.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  function keep(save: InterfaceTemplateSave) {
    const failed = save.configs.filter((one) => !one.isDone)
    setLeft(failed.map((one) => `${one.name}: ${one.message}`))

    if (failed.length === 0) {
      navigate(back)
    }
  }

  return (
    <div>
      {left.length > 0 && (
        <div className="mt-4 text-sm text-alarm">{t("templates.applyFailed", { list: left.join("; ") })}</div>
      )}

      <InterfaceTemplateForm
        start={draftOf(held)}
        held
        pending={change.isPending}
        error={change.error}
        onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(keep)}
        onClose={() => navigate(back)}
        onRemove={may ? () => navigate(`/connections/templates/interfaces/${held.id}/delete`) : undefined}
      />
    </div>
  )
}
