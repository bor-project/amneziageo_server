import { useEffect, useMemo, useState } from "react"
import { QrSheet } from "@/components/QrSheet"
import { primary } from "@/components/styles"
import type { Text } from "@/i18n"
import { draw } from "@/qr"
import type { Picture } from "@/qr"
import { bytes, fill, textOf, words } from "@/subscription/words"
import type { Language, View } from "@/subscription/words"

/**
 * The page a browser gets at the address of a subscription: the cards of the export of the panel, one for the
 * subscription and two for every configuration in it.
 */
export function SubscriptionPage({ view, language }: { view: View | null; language: Language }) {
  const t = useMemo(() => textOf(language), [language])

  return (
    <div className="flex min-h-screen flex-col bg-canvas text-ink">
      <header className="flex min-h-14 items-center border-b border-line bg-chrome px-5 py-2">
        <span className="min-w-0 truncate text-[15px] font-semibold">{view?.title ?? ""}</span>
      </header>
      <main className="flex-1 p-5">
        <div className="mx-auto max-w-[1240px]">
          {view === null ? <p className="text-sm text-muted">{words[language].gone}</p> : <Body view={view} language={language} t={t} />}
        </div>
      </main>
    </div>
  )
}

function Body({ view, language, t }: { view: View; language: Language; t: Text }) {
  const own = words[language]
  const address = location.origin + location.pathname
  const android = /Android/i.test(navigator.userAgent)
  const count = new Intl.PluralRules(language).select(view.configs.length) as keyof typeof own.configs

  return (
    <>
      <div className="flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted">
        <span>
          {own.subscription} <span className="font-medium text-ink">{view.name}</span>
        </span>
        <span>{fill(own.configs[count], { n: view.configs.length })}</span>
        <span>{fill(own.hours, { n: view.hours })}</span>
        <span>
          {fill(own.today, { up: bytes(language, view.upload), down: bytes(language, view.download) })}
          {view.total > 0 ? ` ${fill(own.of, { total: bytes(language, view.total) })}` : ""}
        </span>
      </div>

      {android && (
        <a
          href={`amneziageo://import?url=${encodeURIComponent(address)}`}
          className={`${primary} mt-4 flex w-full justify-center md:inline-flex md:w-auto`}
        >
          {own.open}
        </a>
      )}

      <div className="mt-4 grid grid-cols-1 items-start gap-4 md:grid-cols-2 xl:grid-cols-3">
        <Drawn caption={t("clients.qrSubscription")} text={address} name={`${view.name}-subscription.png`} plain={false} t={t} />
      </div>

      {view.configs.length === 0 && <p className="mt-6 text-sm text-muted">{own.none}</p>}

      {view.configs.map((sheet, index) => (
        <section key={index} className="mt-6">
          <div className="mb-3 flex flex-wrap items-center gap-2">
            <h2 className="min-w-0 truncate text-[15px] font-semibold">{sheet.name}</h2>
            {sheet.interface.length > 0 && <span className="rounded-full bg-chip px-2 py-0.5 text-xs text-chip-ink">{sheet.interface}</span>}
            {sheet.endpoint.length > 0 && <span className="font-mono text-xs text-muted">{sheet.endpoint}</span>}
          </div>
          <div className="grid grid-cols-1 items-start gap-4 md:grid-cols-2 xl:grid-cols-3">
            <Drawn caption={t("clients.qrFile")} text={sheet.text} name={sheet.fileName} plain t={t} />
            <Drawn caption={t("clients.qrLink")} text={sheet.link} name={`${stem(sheet.fileName)}-link.png`} plain={false} t={t} />
          </div>
        </section>
      ))}
    </>
  )
}

// A card of the export whose QR code is drawn once the page is up; nothing stands in its place until then.
function Drawn({ caption, text, name, plain, t }: { caption: string; text: string; name: string; plain: boolean; t: Text }) {
  const [drawn, setDrawn] = useState<{ picture: Picture | null } | null>(null)

  useEffect(() => {
    let live = true
    void draw(text).then((picture) => {
      if (live) {
        setDrawn({ picture })
      }
    })

    return () => {
      live = false
    }
  }, [text])

  if (drawn === null) {
    return null
  }

  return <QrSheet caption={caption} text={text} picture={drawn.picture} name={name} plain={plain} note={null} t={t} />
}

function stem(fileName: string): string {
  return fileName.endsWith(".conf") ? fileName.slice(0, -".conf".length) : fileName
}
