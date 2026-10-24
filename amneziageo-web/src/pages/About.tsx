import { Link } from "react-router-dom"
import { useHealth } from "@/api/health"
import { useTerms } from "@/api/terms"
import { useCrumbs } from "@/components/crumbs"
import { card } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { legalNames } from "@/legal"
import type { LegalName } from "@/legal"

const source = "https://github.com/bor-project/amneziageo_server"

const links: Record<LegalName, TextKey> = {
  terms: "info.termsLink",
  privacy: "info.privacyLink",
  "third-party": "info.thirdPartyLink",
}

export function About() {
  const t = useText()
  const language = useLanguage()
  const health = useHealth()
  const last = useTerms().data?.last

  useCrumbs([{ label: t("info.title") }])

  return (
    <div className="flex max-w-[45rem] flex-col gap-4">
      <h1 className="text-2xl leading-10 font-semibold tracking-[-0.02em]">{t("info.title")}</h1>

      <div className={`${card} grid grid-cols-[minmax(0,10rem)_minmax(0,1fr)] gap-x-4 gap-y-3 p-5 text-sm`}>
        <span className="text-muted">{t("info.version")}</span>
        <span className="font-mono text-[13px]">{health.data?.version ?? "-"}</span>
        <span className="text-muted">{t("info.license")}</span>
        <span>{t("info.licenseValue")}</span>
        <span className="text-muted">{t("info.terms")}</span>
        <span>
          {last
            ? t("info.accepted", {
                version: last.version,
                date: new Date(last.acceptedUtc).toLocaleDateString(language),
                login: last.login,
              })
            : t("info.notAccepted")}
        </span>
        <span className="text-muted">{t("info.updates")}</span>
        <span>{t("info.updatesText")}</span>
      </div>

      <div className="flex flex-col items-start gap-1.5 text-sm">
        {legalNames.map((name) => (
          <Link key={name} to={`/about/${name}`} className="text-brand hover:underline">
            {t(links[name])}
          </Link>
        ))}
        <a href={source} target="_blank" rel="noreferrer" className="text-brand hover:underline">
          {t("info.source")}
        </a>
      </div>
    </div>
  )
}
