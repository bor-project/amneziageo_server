import { useState } from "react"
import { complaint, reason } from "@/api/auth"
import { useBasic, useSaveBasic } from "@/api/rules"
import type { BasicLists, RuleState } from "@/api/rules"
import { scopes } from "@/api/scopes"
import { card, field, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function BasicRouting() {
  const t = useText()
  const basic = useBasic()
  const user = useAppSelector((s) => s.auth.user)
  const may = holds(user, scopes.manageRouting)

  if (basic.data === undefined) {
    return <div className="mt-4 text-sm text-muted">{t("rules.loading")}</div>
  }

  return <Lists key={basic.data.updatedUtc} held={basic.data} may={may} />
}

function Lists({ held, may }: { held: BasicLists; may: boolean }) {
  const t = useText()
  const save = useSaveBasic()
  const [direct, setDirect] = useState(held.direct.join("\n"))
  const [block, setBlock] = useState(held.block.join("\n"))
  const [fault, setFault] = useState<TextKey | null>(null)
  const changed = direct !== held.direct.join("\n") || block !== held.block.join("\n")

  async function keep() {
    setFault(null)
    try {
      await save.mutateAsync({ direct: split(direct), block: split(block) })
    } catch (error) {
      setFault(complaint(error))
    }
  }

  function drop() {
    setFault(null)
    setDirect(held.direct.join("\n"))
    setBlock(held.block.join("\n"))
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <List
          id="basic-block"
          caption={t("basic.block")}
          value={block}
          state={held.blockState}
          may={may}
          onChange={setBlock}
          t={t}
        />
        <List
          id="basic-direct"
          caption={t("basic.direct")}
          value={direct}
          state={held.directState}
          may={may}
          onChange={setDirect}
          t={t}
        />
      </div>

      {fault && <div className="text-sm text-alarm">{t(fault)}</div>}

      {may && (
        <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
          <button type="button" onClick={drop} disabled={!changed || save.isPending} className={secondary}>
            {t("rules.cancel")}
          </button>
          <button type="button" onClick={() => void keep()} disabled={!changed || save.isPending} className={primary}>
            {save.isPending ? t("rules.busy") : t("rules.save")}
          </button>
        </div>
      )}
    </div>
  )
}

function List({
  id,
  caption,
  value,
  state,
  may,
  onChange,
  t,
}: {
  id: string
  caption: string
  value: string
  state: RuleState
  may: boolean
  onChange: (value: string) => void
  t: Text
}) {
  return (
    <div className={`flex flex-col gap-3 p-4.5 ${card}`}>
      <div className="flex items-baseline justify-between gap-3">
        <label htmlFor={id} className="text-sm font-semibold text-ink-soft">
          {caption}
        </label>
        <Standing state={state} t={t} />
      </div>
      <textarea
        id={id}
        rows={14}
        value={value}
        readOnly={!may}
        onChange={(e) => onChange(e.target.value)}
        className={`font-mono text-xs ${field}`}
      />
    </div>
  )
}

function Standing({ state, t }: { state: RuleState; t: Text }) {
  if (state.fault.length > 0) {
    return (
      <span className="text-xs text-alarm" title={state.message}>
        {t(reason(state.fault))}
      </span>
    )
  }

  if (!state.isLive) {
    return null
  }

  return (
    <span className="text-xs text-good">
      {t("rules.live")} · {state.ranges} / {state.names}
    </span>
  )
}

function split(text: string): string[] {
  return text
    .split(/[\s,]+/)
    .map((one) => one.trim())
    .filter((one) => one.length > 0)
}
