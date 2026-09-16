import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useProxies, useRemoveProxy } from "@/api/proxies"
import { useTail } from "@/components/crumbs"
import { proxyKind, proxyPoint } from "@/components/proxy"
import { card, danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { proxyTrail } from "@/pages/trails"

export function ProxyRemove() {
  const t = useText()
  const navigate = useNavigate()
  const { proxyId } = useParams()
  const proxies = useProxies()
  const remove = useRemoveProxy()
  const all = proxies.data ?? []
  const held = all.find((one) => one.id === Number(proxyId))

  useTail(held === undefined ? [] : [proxyTrail(held, all), { label: t("proxies.remove") }])

  if (held === undefined) {
    return proxies.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("proxies.loading")}</div>
    ) : (
      <Navigate to="/connections/proxies" replace />
    )
  }

  return (
    <div className="mt-4 flex max-w-[35rem] flex-col gap-4">
      <h2 className="text-[22px] font-semibold">{t("proxies.removeTitle", { name: held.name })}</h2>

      <div className={`border-alarm-line p-4 ${card}`}>
        <div className="text-[13px] font-semibold text-alarm">{t("action.forever")}</div>
        <div className="mt-1 text-[13px] text-muted">{`${proxyKind(t, held)} · ${held.port} · ${proxyPoint(held)}`}</div>
      </div>

      {remove.error !== null && <div className="text-sm text-alarm">{t(complaint(remove.error))}</div>}

      <div className="flex justify-end gap-2">
        <Link to="/connections/proxies" className={`flex h-10 items-center ${secondary}`}>
          {t("action.backToList")}
        </Link>
        <button
          type="button"
          onClick={() => void remove.mutateAsync(held.id).then(() => navigate("/connections/proxies"))}
          disabled={remove.isPending}
          className={danger}
        >
          {t("proxies.remove")}
        </button>
      </div>
    </div>
  )
}
