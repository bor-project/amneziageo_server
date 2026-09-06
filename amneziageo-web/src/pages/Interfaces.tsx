import { useText } from "@/i18n"

export function Interfaces() {
  const t = useText()

  return <h1 className="text-xl font-semibold">{t("nav.interfaces")}</h1>
}
