import type { Obfuscation } from "@/api/configs"
import { Count, Flag, Line } from "@/components/fields"
import { useText } from "@/i18n"

export function ObfuscationFields({
  id,
  cover,
  onChange,
}: {
  id: string
  cover: Obfuscation
  onChange: (change: Partial<Obfuscation>) => void
}) {
  const t = useText()

  return (
    <>
      <div className="col-span-2 grid grid-cols-3 gap-3">
        <Count
          id={`${id}-jc`}
          caption={t("configs.jc")}
          hint={t("configs.jcHint")}
          value={cover.jc}
          onChange={(value) => onChange({ jc: value })}
        />
        <Count
          id={`${id}-jmin`}
          caption={t("configs.jmin")}
          hint={t("configs.jminHint")}
          value={cover.jmin}
          onChange={(value) => onChange({ jmin: value })}
        />
        <Count
          id={`${id}-jmax`}
          caption={t("configs.jmax")}
          hint={t("configs.jmaxHint")}
          value={cover.jmax}
          onChange={(value) => onChange({ jmax: value })}
        />
      </div>

      <div className="col-span-2 grid grid-cols-4 gap-3">
        <Count
          id={`${id}-s1`}
          caption={t("configs.s1")}
          hint={t("configs.s1Hint")}
          value={cover.s1}
          onChange={(value) => onChange({ s1: value })}
        />
        <Count
          id={`${id}-s2`}
          caption={t("configs.s2")}
          hint={t("configs.s2Hint")}
          value={cover.s2}
          onChange={(value) => onChange({ s2: value })}
        />
        <Count
          id={`${id}-s3`}
          caption={t("configs.s3")}
          hint={t("configs.s3Hint")}
          value={cover.s3}
          onChange={(value) => onChange({ s3: value })}
        />
        <Count
          id={`${id}-s4`}
          caption={t("configs.s4")}
          hint={t("configs.s4Hint")}
          value={cover.s4}
          onChange={(value) => onChange({ s4: value })}
        />
        <Line
          id={`${id}-h1`}
          caption={t("configs.h1")}
          hint={t("configs.h1Hint")}
          value={cover.h1}
          onChange={(value) => onChange({ h1: value })}
        />
        <Line
          id={`${id}-h2`}
          caption={t("configs.h2")}
          hint={t("configs.h2Hint")}
          value={cover.h2}
          onChange={(value) => onChange({ h2: value })}
        />
        <Line
          id={`${id}-h3`}
          caption={t("configs.h3")}
          hint={t("configs.h3Hint")}
          value={cover.h3}
          onChange={(value) => onChange({ h3: value })}
        />
        <Line
          id={`${id}-h4`}
          caption={t("configs.h4")}
          hint={t("configs.h4Hint")}
          value={cover.h4}
          onChange={(value) => onChange({ h4: value })}
        />
      </div>

      <div className="col-span-2 grid grid-cols-3 gap-3">
        <Line
          id={`${id}-padding`}
          caption={t("configs.padding")}
          hint={t("configs.paddingHint")}
          value={cover.contentPaddingAddition}
          onChange={(value) => onChange({ contentPaddingAddition: value })}
        />
        <Line
          id={`${id}-rekey-after`}
          caption={t("configs.rekeyAfter")}
          hint={t("configs.rekeyAfterHint")}
          value={cover.rekeyAfterTime}
          onChange={(value) => onChange({ rekeyAfterTime: value })}
        />
        <Line
          id={`${id}-rekey-timeout`}
          caption={t("configs.rekeyTimeout")}
          hint={t("configs.rekeyTimeoutHint")}
          value={cover.rekeyTimeout}
          onChange={(value) => onChange({ rekeyTimeout: value })}
        />
        <Line
          id={`${id}-reject-after`}
          caption={t("configs.rejectAfter")}
          hint={t("configs.rejectAfterHint")}
          value={cover.rejectAfterTime}
          onChange={(value) => onChange({ rejectAfterTime: value })}
        />
        <Line
          id={`${id}-keepalive-timeout`}
          caption={t("configs.keepaliveTimeout")}
          hint={t("configs.keepaliveTimeoutHint")}
          value={cover.keepaliveTimeout}
          onChange={(value) => onChange({ keepaliveTimeout: value })}
        />
        <Line
          id={`${id}-attempts`}
          caption={t("configs.attempts")}
          hint={t("configs.attemptsHint")}
          value={cover.maxHandshakeAttempts}
          onChange={(value) => onChange({ maxHandshakeAttempts: value })}
        />
      </div>

      <Line
        id={`${id}-i1`}
        caption={t("configs.i1")}
        hint={t("configs.iHint")}
        value={cover.i1 ?? ""}
        onChange={(value) => onChange({ i1: value })}
      />
      <Line
        id={`${id}-i2`}
        caption={t("configs.i2")}
        hint={t("configs.iHint")}
        value={cover.i2 ?? ""}
        onChange={(value) => onChange({ i2: value })}
      />
      <Line
        id={`${id}-i3`}
        caption={t("configs.i3")}
        hint={t("configs.iHint")}
        value={cover.i3 ?? ""}
        onChange={(value) => onChange({ i3: value })}
      />
      <Line
        id={`${id}-i4`}
        caption={t("configs.i4")}
        hint={t("configs.iHint")}
        value={cover.i4 ?? ""}
        onChange={(value) => onChange({ i4: value })}
      />
      <Line
        id={`${id}-i5`}
        caption={t("configs.i5")}
        hint={t("configs.iHint")}
        value={cover.i5 ?? ""}
        onChange={(value) => onChange({ i5: value })}
      />
      <Line
        id={`${id}-header-key`}
        caption={t("configs.headerKey")}
        hint={t("configs.headerKeyHint")}
        value={cover.headerProtectionKey}
        onChange={(value) => onChange({ headerProtectionKey: value.trim() })}
        wide
      />
      <Flag
        id={`${id}-trailers`}
        caption={t("configs.trailers")}
        hint={t("configs.trailersHint")}
        value={cover.randomTrailers}
        onChange={(value) => onChange({ randomTrailers: value })}
      />
      <Flag
        id={`${id}-cookies`}
        caption={t("configs.cookies")}
        hint={t("configs.cookiesHint")}
        value={cover.disableCookies}
        onChange={(value) => onChange({ disableCookies: value })}
      />
    </>
  )
}
