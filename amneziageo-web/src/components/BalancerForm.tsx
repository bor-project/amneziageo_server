import { useState } from "react"
import { complaint } from "@/api/auth"
import type { BalanceStrategy, BalancerDraft } from "@/api/balancers"
import { useOutbounds } from "@/api/outbounds"
import { Flag, Line, Part } from "@/components/fields"
import { card, danger, field, label, primary, quiet, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { same } from "@/store/draftSlice"

export function BalancerForm({
  start,
  pending,
  error,
  onSave,
  onClose,
  onRemove,
}: {
  start: BalancerDraft
  pending: boolean
  error: unknown
  onSave: (draft: BalancerDraft) => void
  onClose: () => void
  onRemove?: () => void
}) {
  const t = useText()
  const outbounds = useOutbounds()
  const [draft, setDraft] = useState(start)
  const rest = (outbounds.data ?? []).filter((one) => !draft.members.includes(one.name))
  const edited = !same(draft, start)

  function put(change: Partial<BalancerDraft>) {
    setDraft({ ...draft, ...change })
  }

  function shift(at: number, step: number) {
    const members = [...draft.members]
    const next = at + step
    ;[members[at], members[next]] = [members[next], members[at]]
    put({ members })
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("balancers.partMain")}>
        <Line
          id="balancer-name"
          caption={t("balancers.name")}
          value={draft.name}
          onChange={(name) => put({ name })}
        />

        <div>
          <label className={label} htmlFor="balancer-strategy">
            {t("balancers.strategy")}
          </label>
          <select
            id="balancer-strategy"
            value={draft.strategy}
            onChange={(e) => put({ strategy: e.target.value as BalanceStrategy })}
            className={`mt-1 ${field}`}
          >
            <option value="priority">{t("balancers.priority")}</option>
            <option value="round">{t("balancers.round")}</option>
            <option value="sticky">{t("balancers.sticky")}</option>
          </select>
        </div>

        <div className="sm:col-span-2">
          <span className={label}>{t("balancers.members")}</span>
          <div className="mt-1 rounded border border-line">
            {draft.members.map((name, at) => (
              <div
                key={name}
                className="flex items-center justify-between border-b border-line px-3 py-2 text-sm text-ink last:border-b-0"
              >
                <span>{name}</span>
                <span className="flex gap-1">
                  <button
                    type="button"
                    onClick={() => shift(at, -1)}
                    disabled={at === 0}
                    className={quiet}
                  >
                    ↑
                  </button>
                  <button
                    type="button"
                    onClick={() => shift(at, 1)}
                    disabled={at === draft.members.length - 1}
                    className={quiet}
                  >
                    ↓
                  </button>
                  <button
                    type="button"
                    onClick={() => put({ members: draft.members.filter((one) => one !== name) })}
                    className={quiet}
                  >
                    ✕
                  </button>
                </span>
              </div>
            ))}

            {rest.length > 0 && (
              <div className="border-t border-line p-2">
                <select
                  id="balancer-member"
                  value=""
                  onChange={(e) => put({ members: [...draft.members, e.target.value] })}
                  className={field}
                >
                  <option value="">{t("balancers.pick")}</option>
                  {rest.map((one) => (
                    <option key={one.id} value={one.name}>
                      {one.name}
                    </option>
                  ))}
                </select>
              </div>
            )}
          </div>
        </div>

        <div className="sm:col-span-2">
          <Flag
            id="balancer-enabled"
            caption={t("balancers.enabled")}
            value={draft.isEnabled}
            onChange={(isEnabled) => put({ isEnabled })}
          />
        </div>
      </Part>

      {error !== null && error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(error) as TextKey)}</div>
      )}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        {onRemove !== undefined && (
          <button type="button" onClick={onRemove} className={`mr-auto ${danger}`}>
            {t("balancers.remove")}
          </button>
        )}
        <button type="button" onClick={onClose} disabled={!edited || pending} className={secondary}>
          {t("balancers.cancel")}
        </button>
        <button
          type="button"
          onClick={() => onSave(draft)}
          disabled={!edited || pending || draft.name.length === 0 || draft.members.length === 0}
          className={primary}
        >
          {pending ? t("balancers.busy") : t("balancers.save")}
        </button>
      </div>
    </div>
  )
}
