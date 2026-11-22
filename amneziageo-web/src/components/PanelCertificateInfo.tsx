import { complaint } from "@/api/auth"
import { useFailure } from "@/api/failure"
import { useDownloadPanelCertificate, usePanelCertificate } from "@/api/panel"
import type { CertificateDetails } from "@/api/panel"
import { Part, Shown } from "@/components/fields"
import { secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"

export function PanelCertificateInfo() {
  const t = useText()
  const certificate = usePanelCertificate()
  const download = useDownloadPanelCertificate()
  const failed = useFailure(certificate)
  const data = certificate.data
  const leaf = data?.certificates[0]

  return (
    <Part title={t("certificate.current")}>
      {failed !== null ? (
        <div className="text-sm text-alarm sm:col-span-2">{t(complaint(failed))}</div>
      ) : data === undefined ? (
        <div className="text-sm text-muted sm:col-span-2">{t("page.loading")}</div>
      ) : !data.enabled || leaf === undefined ? (
        <div className="text-sm text-muted sm:col-span-2">{t("certificate.none")}</div>
      ) : (
        <>
          <Details certificate={leaf} id="panel-leaf" />
          {data.certificates.length > 1 && (
            <details className="sm:col-span-2">
              <summary className="cursor-pointer text-sm text-brand-ink">{t("certificate.chain")}</summary>
              <div className="mt-3 flex flex-col gap-4">
                {data.certificates.slice(1).map((one, index) => (
                  <div key={one.sha256} className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">
                    <Details certificate={one} id={`panel-chain-${index}`} />
                  </div>
                ))}
              </div>
            </details>
          )}
        </>
      )}
      <div className="flex flex-wrap gap-2 sm:col-span-2">
        <button type="button" className={secondary} disabled={certificate.isFetching} onClick={() => {
          download.reset()
          void certificate.refetch()
        }}>{t("certificate.refresh")}</button>
        {leaf !== undefined && failed === null && data?.enabled && (
          <>
            <button type="button" className={secondary} disabled={download.isPending} onClick={() => download.mutate(false)}>
              {t("certificate.downloadLeaf")}
            </button>
            <button type="button" className={secondary} disabled={download.isPending} onClick={() => download.mutate(true)}>
              {t("certificate.downloadChain")}
            </button>
          </>
        )}
      </div>
      {download.isError && <div className="text-sm text-alarm sm:col-span-2">{t(complaint(download.error))}</div>}
    </Part>
  )
}

function Details({ certificate, id }: { certificate: CertificateDetails; id: string }) {
  const t = useText()
  const language = useLanguage()
  const key = certificate.keyBits === null
    ? certificate.keyAlgorithm || certificate.keyOid
    : t("certificate.keyBits", { algorithm: certificate.keyAlgorithm || certificate.keyOid, bits: certificate.keyBits })

  return (
    <>
      <Shown id={`${id}-subject`} caption={t("certificate.subject")} value={certificate.subject} wide />
      <Shown id={`${id}-issuer`} caption={t("certificate.issuer")} value={certificate.issuer} wide />
      <Shown id={`${id}-san`} caption="SAN" value={certificate.subjectAlternativeNames.join(", ") || "—"} wide />
      <Shown id={`${id}-from`} caption={t("certificate.notBefore")} value={new Date(certificate.notBefore).toLocaleString(language)} />
      <Shown id={`${id}-until`} caption={t("certificate.notAfter")} value={new Date(certificate.notAfter).toLocaleString(language)} />
      <Shown id={`${id}-key`} caption={t("certificate.key")} value={key} />
      <Shown id={`${id}-signature`} caption={t("certificate.signature")} value={certificate.signatureAlgorithm} />
      <Shown id={`${id}-sha256`} caption="SHA-256" value={certificate.sha256} wide />
    </>
  )
}
