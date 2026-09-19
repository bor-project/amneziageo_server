import { useState } from "react"
import { complaint } from "@/api/auth"
import { useBalancers } from "@/api/balancers"
import { useClients } from "@/api/clients"
import { useConfigs } from "@/api/configs"
import { useOutbounds } from "@/api/outbounds"
import type { RuleAction, RuleDraft, RuleProtocol } from "@/api/rules"
import { Flag, Line, Multi, Part } from "@/components/fields"
import { card, field, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

export function RuleForm({
  start,
  pending,
  error,
  onSave,
  onClose,
}: {
  start: RuleDraft
  pending: boolean
  error: unknown
  onSave: (draft: RuleDraft) => void
  onClose: () => void
}) {
  const t = useText()
  const outbounds = useOutbounds()
  const balancers = useBalancers()
  const clients = useClients()
  const configs = useConfigs()
  const [draft, setDraft] = useState(start)

  function put(change: Partial<RuleDraft>) {
    setDraft({ ...draft, ...change })
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("rules.partMain")}>
        <Line id="rule-name" caption={t("rules.name")} value={draft.name} onChange={(name) => put({ name })} />

        <div>
          <label className={label} htmlFor="rule-action">
            {t("rules.action")}
          </label>
          <select
            id="rule-action"
            value={draft.action}
            onChange={(e) => put({ action: e.target.value as RuleAction })}
            className={`mt-1 ${field}`}
          >
            <option value="out">{t("rules.actionOut")}</option>
            <option value="direct">{t("rules.actionDirect")}</option>
            <option value="block">{t("rules.actionBlock")}</option>
          </select>
        </div>

        {draft.action === "out" && (
          <div>
            <label className={label} htmlFor="rule-outbound">
              {t("rules.outbound")}
            </label>
            <select
              id="rule-outbound"
              value={draft.outbound}
              onChange={(e) => put({ outbound: e.target.value })}
              className={`mt-1 ${field}`}
            >
              <option value="">{t("rules.pick")}</option>
              <optgroup label={t("rules.outbounds")}>
                {(outbounds.data ?? []).map((outbound) => (
                  <option key={outbound.id} value={outbound.name}>
                    {outbound.name}
                  </option>
                ))}
              </optgroup>
              {(balancers.data ?? []).length > 0 && (
                <optgroup label={t("rules.balancers")}>
                  {(balancers.data ?? []).map((balancer) => (
                    <option key={balancer.id} value={balancer.name}>
                      {balancer.name}
                    </option>
                  ))}
                </optgroup>
              )}
            </select>
          </div>
        )}

        <div>
          <label className={label} htmlFor="rule-protocol">
            {t("rules.protocol")}
          </label>
          <select
            id="rule-protocol"
            value={draft.protocol}
            onChange={(e) => put({ protocol: e.target.value as RuleProtocol })}
            className={`mt-1 ${field}`}
          >
            <option value="any">{t("rules.protocolAny")}</option>
            <option value="tcp">TCP</option>
            <option value="udp">UDP</option>
          </select>
        </div>

        <Line
          id="rule-ports"
          caption={t("rules.ports")}
          value={draft.ports.join(", ")}
          onChange={(text) => put({ ports: split(text) })}
        />

        <Line
          id="rule-source-ports"
          caption={t("rules.sourcePorts")}
          value={draft.sourcePorts.join(", ")}
          onChange={(text) => put({ sourcePorts: split(text) })}
        />

        <div className="flex flex-col gap-2 sm:col-span-2">
          <Flag
            id="rule-enabled"
            caption={t("rules.enabled")}
            value={draft.isEnabled}
            onChange={(isEnabled) => put({ isEnabled })}
          />
          {draft.action === "out" && (
            <Flag
              id="rule-holds"
              caption={t("rules.holdsWhenDown")}
              value={draft.holdsWhenDown}
              onChange={(holdsWhenDown) => put({ holdsWhenDown })}
            />
          )}
        </div>
      </Part>

      <Part title={t("rules.partAddresses")}>
        <Lines
          id="rule-targets"
          caption={t("rules.targets")}
          value={draft.targets}
          onChange={(targets) => put({ targets })}
        />

        <Lines
          id="rule-sources"
          caption={t("rules.sources")}
          value={draft.sources}
          onChange={(sources) => put({ sources })}
        />

        <div>
          <label className={label} htmlFor="rule-clients">
            {t("rules.clients")}
          </label>
          <div className="mt-1">
            <Multi
              id="rule-clients"
              value={draft.clients}
              offers={(clients.data ?? []).map((one) => one.name)}
              placeholder={t("rules.anyone")}
              onChange={(value) => put({ clients: value })}
            />
          </div>
        </div>

        <div>
          <label className={label} htmlFor="rule-inbounds">
            {t("rules.interfaces")}
          </label>
          <div className="mt-1">
            <Multi
              id="rule-inbounds"
              value={draft.inbounds}
              offers={(configs.data ?? []).map((one) => one.name)}
              placeholder={t("rules.anyInbound")}
              onChange={(value) => put({ inbounds: value })}
            />
          </div>
        </div>
      </Part>

      {error !== null && error !== undefined && (
        <div className="text-sm text-alarm">{t(complaint(error) as TextKey)}</div>
      )}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        <button type="button" onClick={onClose} className={secondary}>
          {t("rules.cancel")}
        </button>
        <button
          type="button"
          onClick={() => onSave(draft)}
          disabled={pending || draft.name.length === 0}
          className={primary}
        >
          {pending ? t("rules.busy") : t("rules.save")}
        </button>
      </div>
    </div>
  )
}

function Lines({
  id,
  caption,
  value,
  onChange,
}: {
  id: string
  caption: string
  value: string[]
  onChange: (value: string[]) => void
}) {
  return (
    <div>
      <label className={label} htmlFor={id}>
        {caption}
      </label>
      <textarea
        id={id}
        rows={6}
        value={value.join("\n")}
        onChange={(e) => onChange(split(e.target.value))}
        className={`mt-1 font-mono text-xs ${field}`}
      />
    </div>
  )
}

function split(text: string): string[] {
  return text
    .split(/[\s,]+/)
    .map((one) => one.trim())
    .filter((one) => one.length > 0)
}
