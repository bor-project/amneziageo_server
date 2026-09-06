import { useText } from "@/i18n"

export function Clients() {
  const t = useText()

  return <h1 className="text-xl font-semibold">{t("nav.clients")}</h1>
}
