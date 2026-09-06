import { languageChoices, languageNames, useText } from "@/i18n"
import type { LanguageChoice } from "@/i18n"
import { useAppDispatch, useAppSelector } from "@/store/hooks"
import { languageChosen } from "@/store/uiSlice"

export function LanguagePicker({ className = "" }: { className?: string }) {
  const t = useText()
  const language = useAppSelector((s) => s.ui.language)
  const dispatch = useAppDispatch()

  return (
    <select
      title={t("language.label")}
      aria-label={t("language.label")}
      value={language}
      onChange={(e) => dispatch(languageChosen(e.target.value as LanguageChoice))}
      className={`rounded border border-line bg-surface px-2 py-1 text-sm text-ink outline-none focus:border-brand ${className}`}
    >
      {languageChoices.map((choice) => (
        <option key={choice} value={choice}>
          {choice === "auto" ? t("language.auto") : languageNames[choice]}
        </option>
      ))}
    </select>
  )
}
