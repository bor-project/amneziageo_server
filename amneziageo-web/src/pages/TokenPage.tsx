import { useRef, useState } from "react"
import { Link, useNavigate } from "react-router-dom"
import { maxTokenDays, useMintApiToken } from "@/api/apiTokens"
import type { MintedApiToken } from "@/api/apiTokens"
import { complaint } from "@/api/auth"
import { useRoles } from "@/api/roles"
import { TextBlock } from "@/components/TextBlock"
import { useTail } from "@/components/crumbs"
import { Line, Part, Pick } from "@/components/fields"
import { narrowest, titleOf } from "@/components/roles"
import { card, note, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { selectText } from "@/select"

export function TokenPage() {
  const t = useText()
  const navigate = useNavigate()
  const mint = useMintApiToken()
  const catalog = useRoles()
  const roles = catalog.data?.roles ?? []
  const [name, setName] = useState("")
  const [picked, setPicked] = useState<string | null>(null)
  const [days, setDays] = useState("")
  const [minted, setMinted] = useState<MintedApiToken | null>(null)
  const role = picked ?? narrowest(roles)
  const lifetime = daysOf(days)
  const ready = !mint.isPending && name.trim().length > 0 && role.length > 0 && lifetime !== undefined

  useTail([{ label: t("apiTokens.newTitle") }])

  async function save() {
    setMinted(await mint.mutateAsync({ name: name.trim(), role, days: lifetime ?? null }))
  }

  if (minted !== null) {
    return <Secret minted={minted} />
  }

  return (
    <div className="mt-4 flex max-w-[42rem] flex-col gap-4">
      <Part title={t("apiTokens.title")}>
        <Line id="token-name" caption={t("apiTokens.name")} value={name} onChange={setName} />

        <Pick id="token-role" caption={t("apiTokens.role")} value={role} onChange={setPicked}>
          {roles.map((one) => (
            <option key={one.name} value={one.name}>
              {titleOf(roles, one.name)}
            </option>
          ))}
        </Pick>

        <Line
          id="token-days"
          caption={t("apiTokens.days")}
          value={days}
          placeholder={t("apiTokens.forever")}
          onChange={(value) => setDays(value.trim())}
          fault={lifetime === undefined ? t("error.badTokenLifetime") : ""}
        />
      </Part>

      {mint.error !== null && <div className="text-sm text-alarm">{t(complaint(mint.error))}</div>}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        <button type="button" onClick={() => navigate("/settings/users")} className={secondary}>
          {t("apiTokens.cancel")}
        </button>
        <button type="button" onClick={() => void save()} disabled={!ready} className={primary}>
          {mint.isPending ? t("apiTokens.busy") : t("apiTokens.save")}
        </button>
      </div>
    </div>
  )
}

function Secret({ minted }: { minted: MintedApiToken }) {
  const t = useText()
  const block = useRef<HTMLPreElement>(null)
  const [copied, setCopied] = useState(false)

  async function copy() {
    try {
      await navigator.clipboard.writeText(minted.secret)
      setCopied(true)
    } catch {
      if (block.current !== null) {
        selectText(block.current)
      }
    }
  }

  return (
    <div className="mt-4 flex max-w-[42rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("apiTokens.secretTitle", { name: minted.token.name })}</h2>

      <div className={`p-4 ${card}`}>
        <TextBlock
          ref={block}
          className="rounded-lg border border-line bg-canvas px-3 py-2 font-mono text-sm break-all whitespace-pre-wrap text-ink"
        >
          {minted.secret}
        </TextBlock>
        <div className={note}>{t("apiTokens.once")}</div>
      </div>

      <div className="flex justify-end gap-2">
        <button type="button" onClick={() => void copy()} className={secondary}>
          {copied ? t("apiTokens.copied") : t("apiTokens.copy")}
        </button>
        <Link to="/settings/users" className={`flex h-10 items-center ${primary}`}>
          {t("apiTokens.done")}
        </Link>
      </div>
    </div>
  )
}

function daysOf(text: string): number | null | undefined {
  if (text.length === 0) {
    return null
  }

  if (!/^\d{1,4}$/.test(text)) {
    return undefined
  }

  const days = Number(text)

  return days >= 1 && days <= maxTokenDays ? days : undefined
}
