import { useRef, useState } from "react"
import { maxTokenDays, useApiTokens, useMintApiToken, useRevokeApiToken } from "@/api/apiTokens"
import type { ApiToken, MintedApiToken } from "@/api/apiTokens"
import { complaint } from "@/api/auth"
import { useRoles } from "@/api/roles"
import type { Role } from "@/api/roles"
import { Modal } from "@/components/Modal"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { TextBlock } from "@/components/TextBlock"
import { Line, Pick } from "@/components/fields"
import { card, danger, note, primary, secondary } from "@/components/styles"
import { useLanguage, useText } from "@/i18n"
import { selectText } from "@/select"

export function ApiTokens() {
  const t = useText()
  const language = useLanguage()
  const tokens = useApiTokens()
  const catalog = useRoles()
  const [adding, setAdding] = useState(false)
  const [revoking, setRevoking] = useState<ApiToken | null>(null)

  function stamp(value: string) {
    return new Date(value).toLocaleString(language)
  }

  return (
    <div className={`mt-4 ${card}`}>
      <div className="flex items-center justify-between border-b border-line px-4 py-3">
        <span className="text-sm font-medium text-ink">{t("apiTokens.title")}</span>
        <button type="button" onClick={() => setAdding(true)} className={primary}>
          {t("apiTokens.add")}
        </button>
      </div>

      {tokens.data?.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("apiTokens.empty")}</div>}

      {tokens.data && tokens.data.length > 0 && (
        <Rows
          items={tokens.data}
          keyOf={(token) => token.id}
          columns={[
            {
              key: "name",
              caption: t("apiTokens.name"),
              sort: (token) => token.name,
              lead: true,
              body: "font-medium text-ink",
              cell: (token) => token.name,
            },
            {
              key: "role",
              caption: t("apiTokens.role"),
              sort: (token) => titleOf(catalog.data?.roles ?? [], token.role),
              body: "text-muted",
              cell: (token) => titleOf(catalog.data?.roles ?? [], token.role),
            },
            {
              key: "created",
              caption: t("apiTokens.created"),
              sort: (token) => Date.parse(token.createdUtc),
              body: "whitespace-nowrap text-muted",
              cell: (token) => stamp(token.createdUtc),
            },
            {
              key: "expires",
              caption: t("apiTokens.expires"),
              sort: (token) => (token.expiresUtc === null ? Number.MAX_SAFE_INTEGER : Date.parse(token.expiresUtc)),
              body: "whitespace-nowrap text-muted",
              cell: (token) => (token.expiresUtc === null ? t("apiTokens.forever") : stamp(token.expiresUtc)),
            },
            {
              key: "used",
              caption: t("apiTokens.used"),
              sort: (token) => (token.lastUsedUtc === null ? null : Date.parse(token.lastUsedUtc)),
              body: "whitespace-nowrap text-muted",
              cell: (token) =>
                token.lastUsedUtc === null ? (
                  t("apiTokens.never")
                ) : (
                  <>
                    <div>{stamp(token.lastUsedUtc)}</div>
                    {token.lastAddress !== null && <div className="text-xs">{token.lastAddress}</div>}
                  </>
                ),
            },
            {
              key: "state",
              caption: t("apiTokens.state"),
              sort: (token) => (token.isExpired ? 1 : 0),
              cell: (token) => (
                <span className={token.isExpired ? "text-alarm" : "text-brand-ink"}>
                  {t(token.isExpired ? "apiTokens.expired" : "apiTokens.active")}
                </span>
              ),
            },
            {
              key: "actions",
              caption: t("apiTokens.actions"),
              tail: true,
              cell: (token) => (
                <RowActions
                  title={t("apiTokens.actions")}
                  actions={[{ label: t("apiTokens.revoke"), onPick: () => setRevoking(token), alarming: true }]}
                />
              ),
            },
          ]}
        />
      )}

      {adding && <MintDialog onClose={() => setAdding(false)} />}
      {revoking && <RevokeDialog token={revoking} onClose={() => setRevoking(null)} />}
    </div>
  )
}

function MintDialog({ onClose }: { onClose: () => void }) {
  const t = useText()
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

  async function save() {
    setMinted(await mint.mutateAsync({ name: name.trim(), role, days: lifetime ?? null }))
  }

  if (minted !== null) {
    return <SecretDialog minted={minted} onClose={onClose} />
  }

  return (
    <Modal
      title={t("apiTokens.newTitle")}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("apiTokens.cancel")}
          </button>
          <button type="button" onClick={() => void save()} disabled={!ready} className={primary}>
            {mint.isPending ? t("apiTokens.busy") : t("apiTokens.save")}
          </button>
        </>
      }
    >
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
      <Complaint error={mint.error} />
    </Modal>
  )
}

function SecretDialog({ minted, onClose }: { minted: MintedApiToken; onClose: () => void }) {
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
    <Modal
      title={t("apiTokens.secretTitle", { name: minted.token.name })}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={() => void copy()} className={secondary}>
            {copied ? t("apiTokens.copied") : t("apiTokens.copy")}
          </button>
          <button type="button" onClick={onClose} className={primary}>
            {t("apiTokens.done")}
          </button>
        </>
      }
    >
      <TextBlock
        ref={block}
        className="rounded border border-line bg-canvas px-3 py-2 font-mono text-sm break-all whitespace-pre-wrap text-ink"
      >
        {minted.secret}
      </TextBlock>
      <div className={note}>{t("apiTokens.once")}</div>
    </Modal>
  )
}

function RevokeDialog({ token, onClose }: { token: ApiToken; onClose: () => void }) {
  const t = useText()
  const revoke = useRevokeApiToken()

  async function drop() {
    await revoke.mutateAsync(token.id)
    onClose()
  }

  return (
    <Modal
      title={t("apiTokens.revokeTitle", { name: token.name })}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("apiTokens.cancel")}
          </button>
          <button type="button" onClick={() => void drop()} disabled={revoke.isPending} className={danger}>
            {revoke.isPending ? t("apiTokens.busy") : t("apiTokens.revoke")}
          </button>
        </>
      }
    >
      <Complaint error={revoke.error} />
    </Modal>
  )
}

function Complaint({ error }: { error: unknown }) {
  const t = useText()

  if (!error) {
    return null
  }

  return <div className="rounded bg-alarm-soft px-3 py-2 text-sm text-alarm">{t(complaint(error))}</div>
}

function titleOf(roles: Role[], name: string): string {
  const found = roles.find((one) => one.name === name)

  return found !== undefined && found.title.length > 0 ? found.title : name
}

function narrowest(roles: Role[]): string {
  const sorted = [...roles].sort((a, b) => a.scopes.length - b.scopes.length || a.name.localeCompare(b.name))

  return sorted[0]?.name ?? ""
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
