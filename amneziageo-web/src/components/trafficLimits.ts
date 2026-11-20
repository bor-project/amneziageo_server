import type { TrafficLimits, TrafficPeriod, QuotaAction } from "@/api/trafficLimits"
import type { TextKey } from "@/i18n"

export interface TrafficLimitsInput {
  enabled: boolean
  uploadBitsPerSecond: string
  downloadBitsPerSecond: string
  quotaBytes: string
  period: TrafficPeriod
  quotaAction: QuotaAction
  exceededUploadBitsPerSecond: string
  exceededDownloadBitsPerSecond: string
}

export type TrafficLimitsFaults = Partial<Record<keyof TrafficLimitsInput, TextKey>>

export const bitsPerMegabit = 1_000_000
export const bytesPerGibibyte = 1024 ** 3
export const maxTrafficRate = 1_000_000_000_000
export const maxTrafficQuota = 2 ** 50

const emptyLimits: TrafficLimits = {
  enabled: false,
  uploadBitsPerSecond: 0,
  downloadBitsPerSecond: 0,
  quotaBytes: 0,
  period: "day",
  quotaAction: "throttle",
  exceededUploadBitsPerSecond: 0,
  exceededDownloadBitsPerSecond: 0,
}

export function trafficAmountText(value: number, unit: number): string {
  return value === 0 ? "" : (value / unit).toLocaleString("en-US", { useGrouping: false, maximumFractionDigits: 20 })
}

export function trafficAmount(text: string, unit: number, maximum: number, original?: number): number | null {
  const clean = text.trim()
  if (original !== undefined && Number.isSafeInteger(original) && original >= 0 && original <= maximum && clean === trafficAmountText(original, unit)) {
    return original
  }
  if (clean.length === 0) {
    return 0
  }
  if (clean.length > 256 || !/^\d+([.,]\d+)?$/.test(clean)) {
    return null
  }

  const [whole, fraction = ""] = clean.replace(",", ".").split(".")
  const denominator = 10n ** BigInt(fraction.length)
  const numerator = BigInt(whole + fraction) * BigInt(unit)
  if (numerator > BigInt(maximum) * denominator) {
    return null
  }

  const value = (numerator + denominator / 2n) / denominator

  return value === 0n && numerator > 0n ? null : Number(value)
}

export function trafficLimitsInput(value: TrafficLimits | null | undefined): TrafficLimitsInput {
  const one = value ?? emptyLimits

  return {
    enabled: one.enabled,
    uploadBitsPerSecond: trafficAmountText(one.uploadBitsPerSecond, bitsPerMegabit),
    downloadBitsPerSecond: trafficAmountText(one.downloadBitsPerSecond, bitsPerMegabit),
    quotaBytes: trafficAmountText(one.quotaBytes, bytesPerGibibyte),
    period: one.period,
    quotaAction: one.quotaAction ?? "throttle",
    exceededUploadBitsPerSecond: trafficAmountText(one.exceededUploadBitsPerSecond, bitsPerMegabit),
    exceededDownloadBitsPerSecond: trafficAmountText(one.exceededDownloadBitsPerSecond, bitsPerMegabit),
  }
}

export function readTrafficLimits(input: TrafficLimitsInput, original: TrafficLimits | null): {
  value: TrafficLimits | null
  faults: TrafficLimitsFaults
} {
  const kept = original ?? emptyLimits
  if (!input.enabled) {
    return { value: { ...kept, enabled: false }, faults: {} }
  }

  const faults: TrafficLimitsFaults = {}
  const upload = trafficAmount(input.uploadBitsPerSecond, bitsPerMegabit, maxTrafficRate, kept.uploadBitsPerSecond)
  const download = trafficAmount(input.downloadBitsPerSecond, bitsPerMegabit, maxTrafficRate, kept.downloadBitsPerSecond)
  const quota = trafficAmount(input.quotaBytes, bytesPerGibibyte, maxTrafficQuota, kept.quotaBytes)
  if (upload === null || (upload > 0 && upload < 8)) {
    faults.uploadBitsPerSecond = "error.trafficRate"
  }
  if (download === null || (download > 0 && download < 8)) {
    faults.downloadBitsPerSecond = "error.trafficRate"
  }
  if (quota === null) {
    faults.quotaBytes = "error.trafficQuota"
  }
  if (!["day", "week", "month"].includes(input.period)) {
    faults.period = "error.trafficPeriod"
  }
  if (!["block", "throttle"].includes(input.quotaAction)) {
    faults.quotaAction = "error.trafficQuotaAction"
  }

  const throttled = quota !== null && quota > 0 && input.quotaAction === "throttle"
  const exceededUpload = throttled
    ? trafficAmount(input.exceededUploadBitsPerSecond, bitsPerMegabit, maxTrafficRate, kept.exceededUploadBitsPerSecond)
    : kept.exceededUploadBitsPerSecond
  const exceededDownload = throttled
    ? trafficAmount(input.exceededDownloadBitsPerSecond, bitsPerMegabit, maxTrafficRate, kept.exceededDownloadBitsPerSecond)
    : kept.exceededDownloadBitsPerSecond
  if (exceededUpload === null || (throttled && exceededUpload < 8)) {
    faults.exceededUploadBitsPerSecond = "error.trafficReducedRate"
  }
  if (exceededDownload === null || (throttled && exceededDownload < 8)) {
    faults.exceededDownloadBitsPerSecond = "error.trafficReducedRate"
  }
  if (Object.keys(faults).length > 0) {
    return { value: null, faults }
  }

  return {
    value: {
      enabled: true,
      uploadBitsPerSecond: upload!,
      downloadBitsPerSecond: download!,
      quotaBytes: quota!,
      period: input.period,
      quotaAction: input.quotaAction,
      exceededUploadBitsPerSecond: exceededUpload!,
      exceededDownloadBitsPerSecond: exceededDownload!,
    },
    faults,
  }
}
