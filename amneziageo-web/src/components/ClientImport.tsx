import { useState } from "react"
import { complaint, reason } from "@/api/auth"
import { useImportClients } from "@/api/clients"
import type { ClientImportReport } from "@/api/clients"
import type { Config } from "@/api/configs"
import { Modal } from "@/components/Modal"
import { Line, Pick } from "@/components/fields"
import { field, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function ClientImport({ configs, start, onClose }: { configs: Config[]; start: number; onClose: () => void }) {
  const t = useText()
  const take = useImportClients()
  const [configId, setConfigId] = useState(start > 0 ? start : (configs[0]?.id ?? 0))
  const [text, setText] = useState("")
  const [prefix, setPrefix] = useState("")
  const [report, setReport] = useState<ClientImportReport | null>(null)
  const kept = text.trimStart().startsWith("{")
  const ready = !take.isPending && configId > 0 && text.trim().length > 0

  async function run() {
    setReport(await take.mutateAsync({ configId, text, prefix: kept ? prefix.trim() : "" }))
  }

  if (report !== null) {
    return (
      <Modal
        title={t("clients.importTitle")}
        onClose={onClose}
        footer={
          <button type="button" onClick={onClose} className={primary}>
            {t("clients.importDone")}
          </button>
        }
      >
        <div className="text-sm text-ink">
          {t("clients.imported", { taken: report.taken, held: report.held, refused: report.refused.length })}
        </div>
        {report.renamed.map((one) => (
          <div key={`${one.from}-${one.to}`} className="text-sm text-muted">
            {t("clients.importRenamed", { from: one.from, to: one.to })}
          </div>
        ))}
        {report.refused.map((one, index) => (
          <div key={`${one.name}-${index}`} className="text-sm text-alarm">
            {`${one.name}: ${t(reason(one.error))}`}
          </div>
        ))}
      </Modal>
    )
  }

  return (
    <Modal
      title={t("clients.importTitle")}
      onClose={onClose}
      wide
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("clients.cancel")}
          </button>
          <button type="button" onClick={() => void run()} disabled={!ready} className={primary}>
            {take.isPending ? t("clients.importing") : t("clients.importRun")}
          </button>
        </>
      }
    >
      <Pick
        id="import-config"
        caption={t("clients.endpointName")}
        value={String(configId)}
        onChange={(value) => setConfigId(Number(value))}
      >
        {configs.map((one) => (
          <option key={one.id} value={one.id}>
            {one.name}
          </option>
        ))}
      </Pick>
      <div>
        <label className={label} htmlFor="import-text">
          {t("clients.importText")}
        </label>
        <textarea
          id="import-text"
          value={text}
          rows={10}
          onChange={(e) => setText(e.target.value)}
          className={`mt-1 font-mono text-xs ${field}`}
        />
      </div>
      {kept && <Line id="import-prefix" caption={t("clients.importPrefix")} value={prefix} onChange={setPrefix} />}
      {take.error !== null && take.error !== undefined && (
        <div className="rounded bg-alarm-soft px-3 py-2 text-sm text-alarm">{t(complaint(take.error))}</div>
      )}
    </Modal>
  )
}
