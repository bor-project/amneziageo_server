import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { useClients } from "@/api/clients"
import { useConfigs, useSwitchConfig } from "@/api/configs"
import type { Config } from "@/api/configs"
import { scopes } from "@/api/scopes"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { Knob } from "@/components/fields"
import { card, fieldBox } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Configs() {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const [params, setParams] = useSearchParams()
  const configs = useConfigs()
  const clients = useClients()
  const turn = useSwitchConfig()
  const may = holds(user, scopes.manageInterfaces)
  const find = params.get("find") ?? ""
  const all = configs.data ?? []
  const shown = all.filter((one) => matches(one, find))

  function put(key: string, value: string) {
    const kept = new URLSearchParams(params)

    if (value === "") {
      kept.delete(key)
    } else {
      kept.set(key, value)
    }

    setParams(kept, { replace: true })
  }

  function count(one: Config): number {
    return (clients.data ?? []).filter((client) => client.configId === one.id).length
  }

  return (
    <div className={`mt-4 ${card}`}>
      {all.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("configs.empty")}</div>}

      {all.length > 0 && (
        <Rows
          name="config"
          items={shown}
          keyOf={(one) => one.id}
          tools={
            <input
              value={find}
              placeholder={t("action.search")}
              onChange={(e) => put("find", e.target.value)}
              className={`w-full wide:w-80 ${fieldBox}`}
            />
          }
          columns={[
            {
              key: "on",
              caption: t("action.on"),
              cell: (one) => (
                <Knob
                  value={one.isEnabled}
                  title={one.isEnabled ? t("action.turnOff") : t("action.turnOn")}
                  disabled={!may || turn.isPending}
                  onChange={(on) => void turn.mutateAsync({ id: one.id, on })}
                />
              ),
            },
            {
              key: "name",
              caption: t("configs.name"),
              sort: (one) => one.name,
              lead: true,
              body: "font-semibold text-ink",
              cell: (one) => (
                <Link to={`/connections/interfaces/${one.id}/edit`} className="hover:text-brand-ink">
                  {one.name}
                </Link>
              ),
            },
            {
              key: "endpoint",
              caption: t("configs.endpoint"),
              sort: (one) => (one.host.length > 0 ? `${one.host}:${one.listenPort}` : one.listenPort),
              cell: (one) => (one.host.length > 0 ? `${one.host}:${one.listenPort}` : one.listenPort),
            },
            {
              key: "address",
              caption: t("configs.address"),
              sort: (one) => one.address.join(", "),
              cell: (one) => one.address.join(", "),
            },
            {
              key: "public",
              caption: t("configs.public"),
              sort: (one) => one.publicKey,
              body: "max-w-56",
              cell: (one) => <span className="block truncate font-mono text-xs text-mono">{one.publicKey}</span>,
            },
            {
              key: "count",
              caption: t("configs.count"),
              sort: (one) => count(one),
              cell: (one) => count(one),
            },
            {
              key: "status",
              caption: t("configs.status"),
              sort: (one) => (one.isEnabled ? 0 : 1),
              cell: (one) =>
                one.isEnabled ? (
                  <span className="text-good">{t("configs.running")}</span>
                ) : (
                  <span className="text-muted">{t("configs.stopped")}</span>
                ),
            },
            {
              key: "actions",
              caption: t("configs.actions"),
              tail: true,
              cell: (one) =>
                may && (
                  <RowActions
                    title={t("configs.actions")}
                    actions={[
                      {
                        label: t("action.settings"),
                        onPick: () => navigate(`/connections/interfaces/${one.id}/edit`),
                      },
                    ]}
                  />
                ),
            },
          ]}
        />
      )}
    </div>
  )
}

function matches(one: Config, find: string): boolean {
  const query = find.trim().toLowerCase()

  return (
    query === "" ||
    one.name.toLowerCase().includes(query) ||
    one.host.toLowerCase().includes(query) ||
    one.address.some((address) => address.toLowerCase().includes(query)) ||
    String(one.listenPort).includes(query)
  )
}
