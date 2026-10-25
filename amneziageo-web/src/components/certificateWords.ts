import type { Text, TextKey } from "@/i18n"

const said: Record<string, TextKey> = {
  "certificate-missing": "services.certificateMissing",
  "certificate-unreadable": "services.certificateFile",
  "certificate-expired": "services.certificateRanOut",
  "certificate-name": "services.certificateName",
  "certificate-self-signed": "services.certificateSelfSigned",
  "certificate-no-intermediate": "services.certificateNoIntermediate",
  "certificate-untrusted": "services.certificateUntrusted",
  "certificate-old-android": "services.certificateOldAndroid",
}

/**
 * Says what keeps a certificate from being one every client takes, the day it ran out in the language given; null for
 * a fault that is not about a certificate.
 */
export function certificateWhy(code: string, detail: string, t: Text, language: string): string | null {
  const word = said[code]
  if (!word) {
    return null
  }

  return t(word, { detail: code === "certificate-expired" && detail !== "" ? new Date(detail).toLocaleDateString(language) : detail })
}
