import { Link, Navigate, useNavigate, useParams } from "react-router-dom"
import { useProxies, useProxyCertificate, useSwitchProxy } from "@/api/proxies"
import { scopes } from "@/api/scopes"
import { ProxyState } from "@/components/ProxyState"
import { RowActions } from "@/components/RowActions"
import { useTail } from "@/components/crumbs"
import { Box } from "@/components/fields"
import { proxyFault, proxyKind, proxyPoint } from "@/components/proxy"
import { primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { proxyTrail } from "@/pages/trails"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function ProxyCard() {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const { proxyId } = useParams()
  const proxies = useProxies()
  const tls = useProxyCertificate()
  const turn = useSwitchProxy()
  const may = holds(user, scopes.manageRouting)
  const all = proxies.data ?? []
  const held = all.find((one) => one.id === Number(proxyId))

  useTail(held === undefined ? [] : [proxyTrail(held, all)])

  if (held === undefined) {
    return proxies.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("proxies.loading")}</div>
    ) : (
      <Navigate to="/connections/proxies" replace />
    )
  }

  const fault = proxyFault(t, held, tls.data)

  return (
    <div className="mt-4 flex flex-col gap-5">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <h2 className="truncate text-2xl leading-10 font-semibold tracking-[-0.02em]">{held.name}</h2>
            <span className="rounded-full bg-chip px-2.5 py-1 text-xs">
              <ProxyState one={held} fault={fault} />
            </span>
          </div>
          <div className="text-[13px] text-muted">{`${proxyKind(t, held)} · ${held.port} · ${proxyPoint(held)}`}</div>
        </div>

        {may && (
          <div className="flex shrink-0 items-center gap-2">
            <button
              type="button"
              onClick={() => void turn.mutateAsync({ id: held.id, on: !held.isEnabled })}
              disabled={turn.isPending}
              className={secondary}
            >
              {held.isEnabled ? t("proxies.turnOff") : t("proxies.turnOn")}
            </button>
            <Link to="edit" className={`flex h-10 items-center ${primary}`}>
              {t("proxies.edit")}
            </Link>
            <RowActions
              title={t("proxies.actions")}
              actions={[
                {
                  label: t("proxies.remove"),
                  onPick: () => navigate(`/connections/proxies/${held.id}/delete`),
                  alarming: true,
                },
              ]}
            />
          </div>
        )}
      </div>

      {fault.length > 0 && <div className="text-sm text-alarm">{fault}</div>}

      <div className="grid gap-3.5 [grid-template-columns:repeat(auto-fit,minmax(210px,1fr))]">
        <Box caption={t("proxies.kind")}>
          <div className="text-[15px] text-body">{proxyKind(t, held)}</div>
        </Box>

        <Box caption={t("proxies.port")}>
          <div className="text-[15px] text-body">{held.port}</div>
        </Box>

        <Box caption={t("proxies.opened")}>
          <div className="text-[15px] text-body">{t(held.opened ? "action.yes" : "action.no")}</div>
        </Box>

        <Box caption={held.kind === "wg" ? t("proxies.target") : t("proxies.path")}>
          <div className="text-[15px] break-all text-body">{proxyPoint(held)}</div>
        </Box>

        <Box caption={t("proxies.sources")}>
          <div className="text-[15px] text-body">
            {held.sources.length > 0 ? held.sources.join(", ") : t("proxies.anySource")}
          </div>
        </Box>

        <Box caption={t("proxies.enabled")}>
          <div className="text-[15px] text-body">{t(held.isEnabled ? "action.yes" : "action.no")}</div>
        </Box>

        {held.kind === "ws" && (
          <Box caption={t("proxies.certificate")}>
            <div className="text-[15px] break-all text-body">
              {held.certificate.length > 0 ? held.certificate : (tls.data?.chain ?? "")}
            </div>
          </Box>
        )}

        {held.kind === "ws" && (
          <Box caption={t("proxies.certificateKey")}>
            <div className="text-[15px] break-all text-body">
              {held.certificateKey.length > 0 ? held.certificateKey : (tls.data?.key ?? "")}
            </div>
          </Box>
        )}
      </div>
    </div>
  )
}
