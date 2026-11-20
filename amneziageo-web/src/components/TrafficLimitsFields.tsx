import type { TrafficPeriod, QuotaAction } from "@/api/trafficLimits"
import { Hinted, Line, Pick, Switch } from "@/components/fields"
import { bytesPerGibibyte, maxTrafficQuota, trafficAmount } from "@/components/trafficLimits"
import type { TrafficLimitsFaults, TrafficLimitsInput } from "@/components/trafficLimits"
import { useText } from "@/i18n"

export function TrafficLimitsFields({
  id,
  input,
  onChange,
  faults = {},
  readOnly = false,
}: {
  id: string
  input: TrafficLimitsInput
  onChange?: (value: TrafficLimitsInput) => void
  faults?: TrafficLimitsFaults
  readOnly?: boolean
}) {
  const t = useText()
  const quota = trafficAmount(input.quotaBytes, bytesPerGibibyte, maxTrafficQuota)
  const metered = quota !== 0

  function put(change: Partial<TrafficLimitsInput>) {
    onChange?.({ ...input, ...change })
  }

  return (
    <>
      <div className="sm:col-span-2">
        <Switch
          id={`${id}-enabled`}
          caption={t("trafficLimits.enabled")}
          value={input.enabled}
          onChange={(enabled) => put({ enabled })}
          disabled={readOnly}
        />
      </div>
      {input.enabled && (
        <>
          <Line
            id={`${id}-download`}
            caption={t("trafficLimits.download")}
            value={input.downloadBitsPerSecond}
            onChange={(downloadBitsPerSecond) => put({ downloadBitsPerSecond })}
            placeholder={t("clients.noLimit")}
            fault={faults.downloadBitsPerSecond ? t(faults.downloadBitsPerSecond) : ""}
            disabled={readOnly}
          />
          <Line
            id={`${id}-upload`}
            caption={t("trafficLimits.upload")}
            value={input.uploadBitsPerSecond}
            onChange={(uploadBitsPerSecond) => put({ uploadBitsPerSecond })}
            placeholder={t("clients.noLimit")}
            fault={faults.uploadBitsPerSecond ? t(faults.uploadBitsPerSecond) : ""}
            disabled={readOnly}
          />
          <Line
            id={`${id}-quota`}
            caption={<Hinted caption={t("trafficLimits.quota")} text={t("trafficLimits.quotaHint")} />}
            value={input.quotaBytes}
            onChange={(quotaBytes) => put({ quotaBytes })}
            placeholder={t("clients.noLimit")}
            fault={faults.quotaBytes ? t(faults.quotaBytes) : ""}
            disabled={readOnly}
          />
          {metered && (
            <>
              <div>
                <Pick
                  id={`${id}-period`}
                  caption={<Hinted caption={t("trafficLimits.period")} text={t("trafficLimits.periodHint")} />}
                  value={input.period}
                  onChange={(period) => put({ period: period as TrafficPeriod })}
                  disabled={readOnly}
                >
                  <option value="day">{t("trafficLimits.day")}</option>
                  <option value="week">{t("trafficLimits.week")}</option>
                  <option value="month">{t("trafficLimits.month")}</option>
                </Pick>
                {faults.period && <div className="mt-1 text-xs text-alarm">{t(faults.period)}</div>}
              </div>
              <div className="sm:col-span-2">
                <Pick
                  id={`${id}-action`}
                  caption={t("trafficLimits.quotaAction")}
                  value={input.quotaAction}
                  onChange={(quotaAction) => put({ quotaAction: quotaAction as QuotaAction })}
                  disabled={readOnly}
                >
                  <option value="throttle">{t("trafficLimits.throttle")}</option>
                  <option value="block">{t("trafficLimits.block")}</option>
                </Pick>
                {faults.quotaAction && <div className="mt-1 text-xs text-alarm">{t(faults.quotaAction)}</div>}
              </div>
              {input.quotaAction === "throttle" && (
                <>
                  <Line
                    id={`${id}-exceeded-download`}
                    caption={t("trafficLimits.exceededDownload")}
                    value={input.exceededDownloadBitsPerSecond}
                    onChange={(exceededDownloadBitsPerSecond) => put({ exceededDownloadBitsPerSecond })}
                    fault={faults.exceededDownloadBitsPerSecond ? t(faults.exceededDownloadBitsPerSecond) : ""}
                    disabled={readOnly}
                  />
                  <Line
                    id={`${id}-exceeded-upload`}
                    caption={t("trafficLimits.exceededUpload")}
                    value={input.exceededUploadBitsPerSecond}
                    onChange={(exceededUploadBitsPerSecond) => put({ exceededUploadBitsPerSecond })}
                    fault={faults.exceededUploadBitsPerSecond ? t(faults.exceededUploadBitsPerSecond) : ""}
                    disabled={readOnly}
                  />
                </>
              )}
            </>
          )}
        </>
      )}
    </>
  )
}
