import { LanguagePicker } from "@/components/LanguagePicker"
import { OwnPassword } from "@/components/OwnPassword"
import { card } from "@/components/styles"
import { useText } from "@/i18n"

export function General() {
  const t = useText()

  return (
    <div>
      <h1 className="text-xl font-semibold">{t("nav.general")}</h1>

      <div className={`mt-4 p-4 ${card}`}>
        <div className="flex items-center justify-between gap-4">
          <span className="text-sm text-muted">{t("language.label")}</span>
          <LanguagePicker className="w-56" />
        </div>
      </div>

      <OwnPassword />
    </div>
  )
}
