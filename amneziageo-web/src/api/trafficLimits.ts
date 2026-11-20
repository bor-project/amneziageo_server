export type TrafficPeriod = "day" | "week" | "month"

export type QuotaAction = "block" | "throttle"

export interface TrafficLimitsState {
  policy: TrafficLimits
  inherited: boolean
  templateId: number | null
  startedUtc: string
  endsUtc: string
  rxBytes: number
  txBytes: number
  usedBytes: number
  remainingBytes: number | null
  isExhausted: boolean
  isThrottled: boolean
  isBlocked: boolean
  uploadBitsPerSecond: number
  downloadBitsPerSecond: number
  applied: boolean
  error: string
}

export interface TrafficLimits {
  enabled: boolean
  uploadBitsPerSecond: number
  downloadBitsPerSecond: number
  quotaBytes: number
  period: TrafficPeriod
  quotaAction: QuotaAction
  exceededUploadBitsPerSecond: number
  exceededDownloadBitsPerSecond: number
}
