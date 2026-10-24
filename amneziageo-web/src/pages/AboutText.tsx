import { Navigate, useParams } from "react-router-dom"
import { useCrumbs } from "@/components/crumbs"
import { LegalBody } from "@/components/LegalBody"
import { card } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import { isLegalName, legalText } from "@/legal"

export function AboutText() {
  const t = useText()
  const language = useLanguage()
  const { document } = useParams()
  const text = isLegalName(document) ? legalText(document, language) : null

  useCrumbs([{ label: t("info.title"), to: "/about" }, { label: text?.title ?? "" }])

  if (!text) {
    return <Navigate to="/about" replace />
  }

  return (
    <div className="flex max-w-[45rem] flex-col gap-3">
      <h1 className="text-2xl leading-10 font-semibold tracking-[-0.02em]">{text.title}</h1>
      {text.version && <p className="text-sm text-muted">{text.version}</p>}
      <div className={`${card} p-5`}>
        <LegalBody blocks={text.body} />
      </div>
    </div>
  )
}
