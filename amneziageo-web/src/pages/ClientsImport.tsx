import { useState } from "react"
import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { complaint, reason } from "@/api/auth"
import { useImportClients } from "@/api/clients"
import type { ClientImportReport } from "@/api/clients"
import { useConfigs } from "@/api/configs"
import { useTail } from "@/components/crumbs"
import { Line, Part, Pick } from "@/components/fields"
import { card, field, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function ClientsImport() {
  const t = useText()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const configs = useConfigs().data ?? []
  const take = useImportClients()
  const start = Number(params.get("config") ?? 0)
  const [configId, setConfigId] = useState(start > 0 ? start : 0)
  const [text, setText] = useState("")
  const [prefix, setPrefix] = useState("")
  const [report, setReport] = useState<ClientImportReport | null>(null)
  const chosen = configId > 0 ? configId : (configs[0]?.id ?? 0)
  const kept = text.trimStart().startsWith("{")
  const ready = !take.isPending && chosen > 0 && text.trim().length > 0

  useTail([{ label: t("clients.importTitle") }])

  async function run() {
    setReport(await take.mutateAsync({ configId: chosen, text, prefix: kept ? prefix.trim() : "" }))
  }

  if (report !== null) {
    return (
      <div className="mt-4 flex max-w-[42rem] flex-col gap-4">
        <div className={`flex flex-col gap-1 p-4.5 ${card}`}>
          <div className="text-sm text-ink">
            {t("clients.imported", { taken: report.taken, held: report.held, refused: report.refused.length })}
          </div>
          {report.renamed.map((one) => (
            <div key={`${one.from}-${one.to}`} className="text-[13px] text-muted">
              {t("clients.importRenamed", { from: one.from, to: one.to })}
            </div>
          ))}
          {report.refused.map((one, index) => (
            <div key={`${one.name}-${index}`} className="text-[13px] text-alarm">
              {`${one.name}: ${t(reason(one.error))}`}
            </div>
          ))}
        </div>

        <div className="flex justify-end">
          <Link to="/connections/clients" className={`flex h-10 items-center ${primary}`}>
            {t("clients.importDone")}
          </Link>
        </div>
      </div>
    )
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("clients.importTitle")}>
        <Pick
          id="import-config"
          caption={t("clients.endpointName")}
          value={String(chosen)}
          onChange={(value) => setConfigId(Number(value))}
          wide
        >
          {configs.map((one) => (
            <option key={one.id} value={one.id}>
              {one.name}
            </option>
          ))}
        </Pick>

        <div className="sm:col-span-2">
          <label className={label} htmlFor="import-text">
            {t("clients.importText")}
          </label>
          <textarea
            id="import-text"
            value={text}
            rows={12}
            onChange={(e) => setText(e.target.value)}
            className={`mt-1 font-mono text-xs ${field}`}
          />
        </div>

        {kept && <Line id="import-prefix" caption={t("clients.importPrefix")} value={prefix} onChange={setPrefix} wide />}
      </Part>

      {take.error !== null && take.error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(take.error))}</div>
      )}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        <button type="button" onClick={() => navigate("/connections/clients")} className={secondary}>
          {t("clients.cancel")}
        </button>
        <button type="button" onClick={() => void run()} disabled={!ready} className={primary}>
          {take.isPending ? t("clients.importing") : t("clients.importRun")}
        </button>
      </div>
    </div>
  )
}
