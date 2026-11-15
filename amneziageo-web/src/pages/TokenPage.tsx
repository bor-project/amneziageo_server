import { useState } from "react"
import { useNavigate } from "react-router-dom"
import { maxTokenDays, useMintApiToken } from "@/api/apiTokens"
import type { MintedApiToken } from "@/api/apiTokens"
import { complaint } from "@/api/auth"
import { useRoles } from "@/api/roles"
import { TokenSecret } from "@/components/TokenSecret"
import { useTail } from "@/components/crumbs"
import { Line, Part, Pick } from "@/components/fields"
import { narrowest, titleOf } from "@/components/roles"
import { footer, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function TokenPage() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/settings/users")
  const mint = useMintApiToken()
  const catalog = useRoles()
  const roles = catalog.data?.roles ?? []
  const [name, setName] = useState("")
  const [picked, setPicked] = useState<string | null>(null)
  const [days, setDays] = useState("")
  const [minted, setMinted] = useState<MintedApiToken | null>(null)
  const role = picked ?? narrowest(roles)
  const lifetime = daysOf(days)
  const edited = name.length > 0 || picked !== null || days.length > 0
  const ready = !mint.isPending && name.trim().length > 0 && role.length > 0 && lifetime !== undefined

  useTail([{ label: t("apiTokens.newTitle") }])

  async function save() {
    setMinted(await mint.mutateAsync({ name: name.trim(), role, days: lifetime ?? null }))
  }

  if (minted !== null) {
    return <TokenSecret name={minted.token.name} secret={minted.secret} back={back} />
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
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

      <div className={footer}>
        <button type="button" onClick={() => navigate(back)} disabled={!edited || mint.isPending} className={secondary}>
          {t("apiTokens.cancel")}
        </button>
        <button type="button" onClick={() => void save()} disabled={!ready} className={primary}>
          {mint.isPending ? t("apiTokens.busy") : t("apiTokens.save")}
        </button>
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
