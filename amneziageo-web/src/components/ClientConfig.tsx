import { Link } from "react-router-dom"
import { useQuery } from "@tanstack/react-query"
import { useClientConfig } from "@/api/clients"
import type { Miss } from "@/api/clients"
import { scopes } from "@/api/scopes"
import { AppMark } from "@/components/AppMark"
import { QrSheet } from "@/components/QrSheet"
import { certificateWhy } from "@/components/certificateWords"
import { useLanguage, useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { draw } from "@/qr"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

type Kind = "subscription" | "file" | "link"

interface Missing {
  says: string
  to: string | null
}

const kinds: { kind: Kind; label: TextKey; tail: string }[] = [
  { kind: "subscription", label: "clients.qrSubscription", tail: "-subscription.png" },
  { kind: "file", label: "clients.qrFile", tail: ".conf" },
  { kind: "link", label: "clients.qrLink", tail: "-link.png" },
]

const misses: Record<Miss, TextKey> = {
  off: "clients.subscriptionOff",
  "no-id": "clients.subscriptionNoId",
  "no-key": "clients.subscriptionNoKey",
}

export function ClientConfig({ id, editable }: { id: number; editable: boolean }) {
  const t = useText()
  const language = useLanguage()
  const user = useAppSelector((s) => s.auth.user)
  const config = useClientConfig(id)
  const words: Record<Kind, string> = {
    subscription: config.data?.subscription ?? "",
    file: config.data?.text ?? "",
    link: config.data?.link ?? "",
  }
  const pictures = useQuery({
    queryKey: ["client-qr", id, words.file, words.link, words.subscription],
    queryFn: async () => ({
      subscription: words.subscription.length > 0 ? await draw(words.subscription) : null,
      file: await draw(words.file),
      link: await draw(words.link),
    }),
    enabled: words.file.length > 0,
  })
  const name = config.data?.fileName ?? "client.conf"
  const stem = name.endsWith(".conf") ? name.slice(0, -".conf".length) : name
  const miss = config.data?.subscriptionMiss ?? ""
  const carried = config.data?.subscriptionFault ?? ""
  const asked = config.data?.panelFault ?? ""
  const fix = holds(user, scopes.manageAccess) ? "/settings/certificates" : null
  const offered = kinds.filter((one) => words[one.kind].length > 0 || (one.kind === "subscription" && miss !== ""))

  function missing(kind: Kind): Missing | null {
    if (kind !== "subscription" || miss === "" || words.subscription.length > 0) {
      return null
    }

    if (miss === "off") {
      return { says: t(misses[miss]), to: holds(user, scopes.manageAccess) ? "/settings/subscriptions" : null }
    }

    return { says: t(misses[miss]), to: miss === "no-id" && editable ? `/clients/${id}/settings` : null }
  }

  function warning(kind: Kind): string | undefined {
    if (kind === "subscription") {
      return carried === "" || words.subscription.length === 0
        ? undefined
        : t("clients.subscriptionCertificate", { why: why(carried, config.data?.subscriptionDetail ?? "") })
    }

    return asked === "" ? undefined : t("clients.panelCertificate", { why: why(asked, config.data?.panelDetail ?? "") })
  }

  function why(code: string, detail: string): string {
    return certificateWhy(code, detail, t, language) ?? code
  }

  function note(kind: Kind) {
    const gap = missing(kind)
    if (gap === null) {
      return null
    }

    return (
      <>
        {gap.says}
        {gap.to !== null && (
          <Link to={gap.to} className="text-brand-ink hover:text-brand-lit">
            {t("action.settings")}
          </Link>
        )}
      </>
    )
  }

  return (
    <div className="grid grid-cols-1 items-start gap-4 md:grid-cols-2 xl:grid-cols-3">
      {offered.map((one) => (
        <QrSheet
          key={one.kind}
          caption={t(one.label)}
          text={words[one.kind]}
          picture={pictures.data?.[one.kind] ?? null}
          name={one.kind === "file" ? name : stem + one.tail}
          plain={one.kind === "file"}
          note={note(one.kind)}
          mark={one.kind === "subscription" ? <AppMark what="subscriptionQr" /> : undefined}
          warning={warning(one.kind)}
          locked={one.kind === "subscription" && carried !== ""}
          fix={fix}
          t={t}
        />
      ))}
    </div>
  )
}
