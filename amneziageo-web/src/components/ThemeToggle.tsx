import { themeChoices, useTheme } from "@/theme/theme"
import type { ThemeChoice } from "@/theme/theme"
import { useText } from "@/i18n"
import { useAppDispatch, useAppSelector } from "@/store/hooks"
import { themeChosen } from "@/store/uiSlice"

export function ThemeToggle() {
  const t = useText()
  const chosen = useAppSelector((s) => s.ui.theme)
  const theme = useTheme()
  const dispatch = useAppDispatch()
  const next = themeChoices[(themeChoices.indexOf(chosen) + 1) % themeChoices.length]

  return (
    <button
      type="button"
      title={`${t("theme.label")}: ${t(`theme.${chosen}`)}`}
      aria-label={t("theme.label")}
      onClick={() => dispatch(themeChosen(next))}
      className="rounded p-1 text-muted hover:bg-hover hover:text-brand-ink"
    >
      <Glyph choice={chosen} dark={theme === "dark"} />
    </button>
  )
}

function Glyph({ choice, dark }: { choice: ThemeChoice; dark: boolean }) {
  if (choice === "auto") {
    return (
      <svg viewBox="0 0 24 24" className="size-5" fill="none" stroke="currentColor" strokeWidth="1.6">
        <circle cx="12" cy="12" r="9" />
        <path d="M12 3a9 9 0 0 1 0 18z" fill="currentColor" stroke="none" />
      </svg>
    )
  }

  return dark ? <Moon /> : <Sun />
}

function Moon() {
  return (
    <svg viewBox="0 0 24 24" className="size-5" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d="M20.5 14.3A8.6 8.6 0 0 1 9.7 3.5a8.6 8.6 0 1 0 10.8 10.8z" strokeLinejoin="round" />
    </svg>
  )
}

function Sun() {
  return (
    <svg viewBox="0 0 24 24" className="size-5" fill="none" stroke="currentColor" strokeWidth="1.6">
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4.9 4.9l1.5 1.5M17.6 17.6l1.5 1.5M2 12h2M20 12h2M4.9 19.1l1.5-1.5M17.6 6.4l1.5-1.5" strokeLinecap="round" />
    </svg>
  )
}
