import { useState } from "react"
import { useQuery } from "@tanstack/react-query"
import { complaint } from "@/api/auth"
import type { StoreStatus } from "@/api/clientUpdates"
import { useFailure } from "@/api/failure"
import { ReleaseList } from "@/components/ReleaseList"
import { Part, Shown } from "@/components/fields"
import { field, label, note, secondary } from "@/components/styles"
import { bytes } from "@/format"
import { useText } from "@/i18n"
import { copyText } from "@/qr"
import { http, releases } from "./http"

export function Home() {
  const t = useText()
  const status = useQuery({
    queryKey: ["updates-status"],
    queryFn: async () => (await http.get<StoreStatus>("/status")).data,
  })
  const failed = useFailure(status)
  const [copied, setCopied] = useState(false)
  const url = `${window.location.origin}${releases}`

  async function copy() {
    setCopied(await copyText(url))
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("updates.server")}>
        <div className="sm:col-span-2">
          <label className={label} htmlFor="updates-address">
            {t("updates.address")}
          </label>
          <div className="mt-1 flex gap-2">
            <input id="updates-address" value={url} readOnly className={`min-w-0 flex-1 ${field}`} />
            <button type="button" className={secondary} onClick={() => void copy()}>
              {copied ? t("clientUpdates.copied") : t("clientUpdates.copy")}
            </button>
          </div>
        </div>

        <div className="sm:col-span-2">
          <Shown id="updates-directory" caption={t("clientUpdates.directory")} value={status.data?.directory ?? ""} />
          {status.data !== undefined && <Room status={status.data} />}
          {failed !== null && <div className="mt-1 text-xs text-alarm">{t(complaint(failed))}</div>}
        </div>
      </Part>

      <div className="mt-5">
        <ReleaseList may />
      </div>
    </div>
  )
}

// The room and the access of the directory of the releases.
function Room({ status }: { status: StoreStatus }) {
  const t = useText()

  if (!status.writable) {
    return <div className="mt-1 text-xs text-alarm">{`✗ ${status.fault}`}</div>
  }

  return (
    <div className={note}>
      {`${t("clientUpdates.free", { size: bytes(t, status.freeBytes) })} · `}
      <span className="text-good">✓</span>
      {` ${t("clientUpdates.writable")}`}
    </div>
  )
}
