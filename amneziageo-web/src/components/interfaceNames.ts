import type { Text } from "@/i18n"

export function interfaceNameValue(name: string): string {
  return name.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, "")
}

export function interfaceNameFault(t: Text, name: string, taken: string[]): string {
  const value = interfaceNameValue(name)
  if (value.length === 0) {
    return t("error.interfaceNameEmpty")
  }
  if (new TextEncoder().encode(value).length > 15) {
    return t("error.interfaceNameBytes")
  }
  if (value === "." || value === ".." || /[\p{White_Space}\p{Cc}/:,"\\*%]/u.test(value)) {
    return t("error.badInterfaceName")
  }
  return taken.includes(value) ? t("error.nameTaken") : ""
}
