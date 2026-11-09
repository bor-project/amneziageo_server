import type { OutboundKind } from "@/api/outbounds"
import type { TextKey } from "@/i18n"

export function kindKey(kind: OutboundKind): TextKey {
  if (kind === "wg") {
    return "outbounds.kindWg"
  }

  return kind === "ws" ? "outbounds.kindWs" : "outbounds.kindLocal"
}
