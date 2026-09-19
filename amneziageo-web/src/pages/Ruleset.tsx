import { useRuleset } from "@/api/rules"
import { card } from "@/components/styles"
import { TextBlock } from "@/components/TextBlock"
import { useText } from "@/i18n"

export function Ruleset() {
  const t = useText()
  const ruleset = useRuleset()

  if (ruleset.data === undefined) {
    return <div className="mt-4 text-sm text-muted">{t("rules.loading")}</div>
  }

  return (
    <div className={`mt-4 overflow-x-auto ${card}`}>
      <TextBlock className="p-4 font-mono text-xs leading-5 text-body outline-none">{ruleset.data}</TextBlock>
    </div>
  )
}
