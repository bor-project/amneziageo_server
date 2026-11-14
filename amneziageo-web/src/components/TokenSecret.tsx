import { useRef, useState } from "react"
import { Link } from "react-router-dom"
import { TextBlock } from "@/components/TextBlock"
import { card, note, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { selectText } from "@/select"

export function TokenSecret({ name, secret, back }: { name: string; secret: string; back: string }) {
  const t = useText()
  const block = useRef<HTMLPreElement>(null)
  const [copied, setCopied] = useState(false)

  async function copy() {
    try {
      await navigator.clipboard.writeText(secret)
      setCopied(true)
    } catch {
      if (block.current !== null) { selectText(block.current) }
    }
  }

  return <div className="mt-4 flex flex-col gap-4">
    <h2 className="text-[22px] font-semibold">{t("apiTokens.secretTitle", { name })}</h2>
    <div className={`p-4 ${card}`}>
      <TextBlock ref={block} className="rounded-lg border border-line bg-canvas px-3 py-2 font-mono text-sm break-all whitespace-pre-wrap text-ink">{secret}</TextBlock>
      <div className={note}>{t("apiTokens.once")}</div>
    </div>
    <div className="flex justify-end gap-2">
      <button type="button" onClick={() => void copy()} className={secondary}>{copied ? t("apiTokens.copied") : t("apiTokens.copy")}</button>
      <Link to={back} className={`flex h-10 items-center ${primary}`}>{t("apiTokens.done")}</Link>
    </div>
  </div>
}
