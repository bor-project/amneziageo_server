import { useState } from "react"
import { Link, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useProxies } from "@/api/proxies"
import { useMintProxyToken, useProxyTokens, useRevokeProxyToken } from "@/api/proxyTokens"
import type { MintedProxyToken } from "@/api/proxyTokens"
import { TokenSecret } from "@/components/TokenSecret"
import { Line, Part } from "@/components/fields"
import { useTail } from "@/components/crumbs"
import { danger, footer, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function ProxyTokens({ id, disabled }: { id: number; disabled: boolean }) {
  const t = useText()
  const tokens = useProxyTokens(id)
  return <Part title={t("proxies.tokens")}>
    {tokens.data?.length === 0 && <div className="text-sm text-muted sm:col-span-2">{t("proxies.tokensEmpty")}</div>}
    {tokens.data?.map((token) => <div key={token.id} className="flex items-center justify-between gap-3 sm:col-span-2">
      <span className="min-w-0 truncate text-sm">{token.name}</span>
      {!disabled && <Link className={secondary} to={`/routing/proxies/${id}/tokens/${token.id}/delete`}>{t("proxies.revokeToken")}</Link>}
    </div>)}
    {tokens.error !== null && <div className="text-sm text-alarm sm:col-span-2">{t(complaint(tokens.error))}</div>}
    <div className="sm:col-span-2">{disabled ? <button type="button" className={secondary} disabled>{t("proxies.newToken")}</button>
      : <Link className={`inline-flex ${secondary}`} to={`/routing/proxies/${id}/tokens/new`}>{t("proxies.newToken")}</Link>}</div>
  </Part>
}

export function ProxyTokenPage() {
  const { proxyId } = useParams()
  const id = Number(proxyId)
  const t = useText()
  const all = useProxies()
  const held = all.data?.find((proxy) => proxy.id === id)
  const mint = useMintProxyToken(id)
  const [name, setName] = useState("")
  const [minted, setMinted] = useState<MintedProxyToken | null>(null)
  const back = `/routing/proxies/${id}/edit`
  useTail([{ label: held?.name ?? "", to: back }, { label: t("proxies.newToken") }])
  if (minted !== null) { return <TokenSecret name={minted.token.name} secret={minted.secret} back={back} /> }
  return <div className="mt-4 flex flex-col gap-4">
    <Part title={t("proxies.tokens")}><Line id="proxy-token-name" caption={t("apiTokens.name")} value={name} onChange={setName} /></Part>
    {mint.error !== null && <div className="text-sm text-alarm">{t(complaint(mint.error))}</div>}
    <div className={footer}>
      <Link className={secondary} to={back}>{t("geo.cancel")}</Link>
      <button type="button" className={primary} disabled={held === undefined || name.trim().length === 0 || name.trim().length > 64 || mint.isPending}
        onClick={() => void mint.mutateAsync(name.trim()).then(setMinted)}>{t("apiTokens.save")}</button>
    </div>
  </div>
}

export function ProxyTokenRemove() {
  const { proxyId, tokenId } = useParams()
  const id = Number(proxyId)
  const t = useText()
  const navigate = useNavigate()
  const all = useProxies()
  const tokens = useProxyTokens(id)
  const token = tokens.data?.find((one) => one.id === tokenId)
  const revoke = useRevokeProxyToken(id)
  const back = `/routing/proxies/${id}/edit`
  useTail([{ label: all.data?.find((proxy) => proxy.id === id)?.name ?? "", to: back }, { label: t("proxies.revokeToken") }])
  return <div className="mt-4 flex flex-col gap-4">
    <h2 className="text-[22px] font-semibold">{t("proxies.revokeTokenQuestion", { name: token?.name ?? "" })}</h2>
    {revoke.error !== null && <div className="text-sm text-alarm">{t(complaint(revoke.error))}</div>}
    <div className={footer}><Link className={secondary} to={back}>{t("geo.cancel")}</Link>
      <button type="button" className={danger} disabled={token === undefined || revoke.isPending}
        onClick={() => void revoke.mutateAsync(tokenId!).then(() => navigate(back))}>{t("proxies.revokeToken")}</button></div>
  </div>
}
