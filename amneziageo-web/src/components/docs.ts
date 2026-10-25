import { scopes } from "@/api/scopes"
import { useUpdate } from "@/api/update"
import { useLanguage } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

const site = "https://bor-project.github.io/amneziageo_server"

// A part of a page of the panel on the site, for the way the panel runs and in its language.
export function usePanelDocs(page: string, ru: string, en: string): string {
  const language = useLanguage()
  const user = useAppSelector((s) => s.auth.user)
  const mode = useUpdate(holds(user, scopes.readState)).data?.mode
  const kind = mode === "docker" ? "docker" : "service"
  const english = page === "index" ? "" : `${page}.html`

  return language === "ru" ? `${site}/${kind}/panel/${page}.ru.html#${ru}` : `${site}/${kind}/panel/${english}#${en}`
}
