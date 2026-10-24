import privacyEn from "../../PRIVACY.md?raw"
import privacyRu from "../../PRIVACY.ru.md?raw"
import termsEn from "../../TERMS.md?raw"
import termsRu from "../../TERMS.ru.md?raw"
import thirdPartyEn from "../../THIRD-PARTY.md?raw"
import thirdPartyRu from "../../THIRD-PARTY.ru.md?raw"
import type { Language } from "@/i18n"

export type LegalName = "terms" | "privacy" | "third-party"

export interface LegalBlock {
  kind: "heading" | "paragraph" | "bullet"
  text: string
}

export interface LegalText {
  title: string
  version: string
  body: LegalBlock[]
}

export const legalNames: LegalName[] = ["terms", "privacy", "third-party"]

const texts: Record<Language, Record<LegalName, string>> = {
  en: { terms: termsEn, privacy: privacyEn, "third-party": thirdPartyEn },
  ru: { terms: termsRu, privacy: privacyRu, "third-party": thirdPartyRu },
}

const versionLine = /^(Версия|Version) \d+/

export function isLegalName(value: string | undefined): value is LegalName {
  return value === "terms" || value === "privacy" || value === "third-party"
}

// Splits a text of the root of the repository into its title, the line of its version and the headings, paragraphs
// and items under them.
export function legalText(name: LegalName, language: Language): LegalText {
  const text: LegalText = { title: "", version: "", body: [] }
  let open: LegalBlock | null = null

  for (const raw of texts[language][name].split("\n")) {
    const line = raw.trim()
    if (line.length === 0) {
      open = null
    } else if (line.startsWith("# ")) {
      text.title = line.slice(2)
      open = null
    } else if (line.startsWith("## ")) {
      text.body.push({ kind: "heading", text: line.slice(3) })
      open = null
    } else if (line.startsWith("- ")) {
      open = { kind: "bullet", text: line.slice(2) }
      text.body.push(open)
    } else if (open) {
      open.text = `${open.text} ${line}`
    } else if (text.version.length === 0 && text.body.length === 0 && versionLine.test(line)) {
      text.version = line
    } else {
      open = { kind: "paragraph", text: line }
      text.body.push(open)
    }
  }

  return text
}
