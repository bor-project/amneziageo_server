import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { reason } from "@/api/auth"
import { useMoveRule, useRules, useSwitchRule } from "@/api/rules"
import type { Rule } from "@/api/rules"
import { scopes } from "@/api/scopes"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, fieldBox } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Rules() {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const [params, setParams] = useSearchParams()
  const rules = useRules()
  const move = useMoveRule()
  const turn = useSwitchRule()
  const may = holds(user, scopes.manageRouting)
  const find = params.get("find") ?? ""
  const all = rules.data ?? []
  const shown = all.filter((one) => matches(one, find))
  const last = all.length - 1

  function put(key: string, value: string) {
    const kept = new URLSearchParams(params)

    if (value === "") {
      kept.delete(key)
    } else {
      kept.set(key, value)
    }

    setParams(kept, { replace: true })
  }

  return (
    <div className={`mt-4 ${card}`}>
      {all.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("rules.empty")}</div>}

      {all.length > 0 && (
        <Rows
          name="rule"
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
              key: "name",
              caption: t("rules.name"),
              sort: (one) => one.name,
              lead: true,
              body: "font-semibold text-ink",
              cell: (one) => (
                <>
                  {may ? (
                    <Link to={`/routing/rules/${one.id}/edit`} className="hover:text-brand-ink">
                      {one.name}
                    </Link>
                  ) : (
                    one.name
                  )}
                  {!one.isEnabled && <span className="ml-2 text-xs text-muted">{t("rules.off")}</span>}
                </>
              ),
            },
            {
              key: "action",
              caption: t("rules.action"),
              sort: (one) => (one.action === "block" ? t("rules.actionBlock") : one.outbound),
              cell: (one) => (one.action === "block" ? t("rules.actionBlock") : one.outbound),
            },
            {
              key: "targets",
              caption: t("rules.targets"),
              sort: (one) => (one.targets.length > 0 ? one.targets.join(", ") : t("rules.anything")),
              body: "max-w-72",
              cell: (one) => (
                <span className="block truncate" title={one.targets.join(", ")}>
                  {one.targets.length > 0 ? one.targets.join(", ") : t("rules.anything")}
                </span>
              ),
            },
            {
              key: "sources",
              caption: t("rules.sources"),
              sort: (one) => (one.sources.length > 0 ? one.sources.join(", ") : t("rules.anyone")),
              body: "max-w-48",
              cell: (one) => (
                <span className="block truncate" title={one.sources.join(", ")}>
                  {one.sources.length > 0 ? one.sources.join(", ") : t("rules.anyone")}
                </span>
              ),
            },
            {
              key: "traffic",
              caption: t("rules.traffic"),
              sort: (one) => traffic(one, t),
              cell: (one) => traffic(one, t),
            },
            {
              key: "ranges",
              caption: t("rules.ranges"),
              sort: (one) => `${one.state.ranges} / ${one.state.names}`,
              cell: (one) => `${one.state.ranges} / ${one.state.names}`,
            },
            {
              key: "state",
              caption: t("rules.state"),
              sort: (one) => ranked(one),
              cell: (one) => <State rule={one} t={t} />,
            },
            {
              key: "actions",
              caption: t("rules.actions"),
              tail: true,
              cell: (one, at) =>
                may && (
                  <RowActions
                    title={t("rules.actions")}
                    actions={[
                      {
                        label: one.isEnabled ? t("rules.turnOff") : t("rules.turnOn"),
                        onPick: () => void turn.mutateAsync({ id: one.id, on: !one.isEnabled }),
                      },
                      { label: t("rules.edit"), onPick: () => navigate(`/routing/rules/${one.id}/edit`) },
                      ...(at > 0
                        ? [{ label: t("rules.up"), onPick: () => void move.mutateAsync({ id: one.id, up: true }) }]
                        : []),
                      ...(at < last
                        ? [{ label: t("rules.down"), onPick: () => void move.mutateAsync({ id: one.id, up: false }) }]
                        : []),
                      {
                        label: t("rules.remove"),
                        onPick: () => navigate(`/routing/rules/${one.id}/delete`),
                        alarming: true,
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

function ranked(rule: Rule): number {
  if (rule.state.fault.length > 0) {
    return 1
  }

  return rule.state.isLive ? 0 : 2
}

function State({ rule, t }: { rule: Rule; t: Text }) {
  if (rule.state.fault.length > 0) {
    return (
      <span className="text-alarm" title={rule.state.message}>
        {t(reason(rule.state.fault))}
      </span>
    )
  }

  if (!rule.state.isLive) {
    return <span className="text-muted">{t("rules.off")}</span>
  }

  return <span className="text-good">{t("rules.live")}</span>
}

function traffic(rule: Rule, t: Text): string {
  const protocol = rule.protocol === "any" ? t("rules.protocolAny") : rule.protocol.toUpperCase()

  return rule.ports.length > 0 ? `${protocol} ${rule.ports.join(", ")}` : protocol
}

function matches(one: Rule, find: string): boolean {
  const query = find.trim().toLowerCase()

  return (
    query === "" ||
    one.name.toLowerCase().includes(query) ||
    one.outbound.toLowerCase().includes(query) ||
    one.targets.some((target) => target.toLowerCase().includes(query)) ||
    one.sources.some((source) => source.toLowerCase().includes(query))
  )
}
