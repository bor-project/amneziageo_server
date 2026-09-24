import type { ClientBatch } from "@/api/clients"
import type { Text, TextKey } from "@/i18n"

export interface Summary {
  text: string
  title: string
}

// Tells what a command over many clients left undone, empty when it went through whole.
export function summary(
  t: Text,
  answer: ClientBatch,
  total: number,
  done: TextKey,
  nameOf: (id: number) => string,
): Summary {
  if (answer.failed.length === 0 && answer.unsynced.length === 0) {
    return { text: "", title: "" }
  }

  const parts = [t(done, { done: answer.done.length, total })]
  if (answer.failed.length > 0) {
    parts.push(t("clients.notDone", { list: answer.failed.map((one) => nameOf(one.id)).join(", ") }))
  }

  if (answer.unsynced.length > 0) {
    parts.push(t("clients.notTaken", { list: answer.unsynced.map((one) => one.config).join(", ") }))
  }

  const reasons = [
    ...answer.failed.map((one) => `${nameOf(one.id)}: ${one.message}`),
    ...answer.unsynced.map((one) => `${one.config}: ${one.message}`),
  ]

  return { text: parts.join(" "), title: reasons.join("\n") }
}
