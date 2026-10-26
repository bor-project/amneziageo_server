import type { TextKey } from "@/i18n"

export type PresetReach = "all" | "udp"

// What a preset sends through the VPN beyond its lists.
export function reachOf(preset: { full: boolean; allUdp: boolean }): PresetReach | null {
  if (preset.full) {
    return "all"
  }

  return preset.allUdp ? "udp" : null
}

export const reachMarks: Record<PresetReach, TextKey> = {
  all: "presets.markFull",
  udp: "presets.markAllUdp",
}

export const reachWarnings: Record<PresetReach, { title: TextKey; text: TextKey }> = {
  all: { title: "presets.fullTitle", text: "presets.fullNotice" },
  udp: { title: "presets.allUdpTitle", text: "presets.allUdpNotice" },
}
