import { useRef, useState } from "react"
import { asked } from "@/components/trouble"
import type { Trouble } from "@/components/trouble"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { selectText } from "@/select"

// The block that names what keeps the clients from the resolver of the panel, with the commands that mend it.
export function ResolverTrouble({ trouble, kept }: { trouble: Trouble | null; kept: TextKey }) {
  const t = useText()

  if (trouble === null) {
    return null
  }

  return (
    <div role="alert" className="mt-2 rounded-md border border-alarm-line bg-alarm-soft px-3 py-2 text-xs leading-relaxed text-alarm">
      {trouble.kind === "off" && (
        <>
          <div>{t("templates.troubleOff")}</div>
          <Command text="sudo amneziageo-server dns on" />
          <div className="mt-1 text-ink-soft">{t("dns.troubleOffMenu")}</div>
        </>
      )}
      {trouble.kind === "port" && <div>{t("templates.troublePort", { port: trouble.port })}</div>}
      {trouble.kind === "kept" && (
        <>
          <div>{t(kept, { names: trouble.names.join(", ") })}</div>
          <Command text="sudo amneziageo-server dns open" />
          <div className="mt-1 text-ink-soft">{t("dns.troubleKeptMenu")}</div>
          <div className="mt-1 text-ink-soft">{t("dns.troubleKeptHand")}</div>
          {trouble.names.map((name) => (
            <Command key={name} text={`sudo ufw allow in on ${name} to any port ${asked}`} />
          ))}
        </>
      )}
    </div>
  )
}

// One command with the button that copies it.
function Command({ text }: { text: string }) {
  const t = useText()
  const line = useRef<HTMLSpanElement>(null)
  const [copied, setCopied] = useState(false)

  async function copy() {
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
    } catch {
      if (line.current !== null) {
        selectText(line.current)
      }
    }
  }

  const told = t(copied ? "apiTokens.copied" : "apiTokens.copy")

  return (
    <div className="mt-1 flex w-fit max-w-full items-center gap-2 rounded-md border border-line bg-surface px-2 py-0.5 font-mono text-ink">
      <span ref={line} className="break-all">
        {text}
      </span>
      <button
        type="button"
        onClick={() => void copy()}
        title={told}
        aria-label={told}
        className="shrink-0 text-muted hover:text-ink"
      >
        <svg viewBox="0 0 24 24" className="size-3.5" fill="none" stroke="currentColor" strokeWidth="1.8">
          <rect x="8.5" y="8.5" width="11" height="11" rx="2" />
          <path d="M15.5 8.5V6.5a2 2 0 0 0-2-2h-7a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h2" />
        </svg>
      </button>
    </div>
  )
}
