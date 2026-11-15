import { useState } from "react"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { complaint } from "@/api/auth"
import { client } from "@/api/client"
import { RowActions } from "@/components/RowActions"
import { HeadActions } from "@/components/Section"
import { useText } from "@/i18n"

export function RestoreRoutingDefaults({ part, head = false }: { part: "groups" | "presets"; head?: boolean }) {
  const t = useText()
  const queries = useQueryClient()
  const [count, setCount] = useState<number | null>(null)
  const restore = useMutation({
    mutationFn: async () => (await client.post<{ groups: number; presets: number }>(
      part === "groups" ? "/address-groups/defaults" : "/templates/routing/defaults",
    )).data,
    onSuccess: async (answer) => {
      setCount(answer.groups + answer.presets)
      await queries.invalidateQueries({ queryKey: [part === "groups" ? "address-groups" : "presets"] })
    },
  })
  const menu = <fieldset disabled={restore.isPending}>
    <RowActions title={t("rules.actions")} actions={[{
      label: t(restore.isPending ? "routing.defaultsRestoring" : "routing.restoreDefaults"),
      onPick: () => { setCount(null); void restore.mutateAsync().catch(() => undefined) },
    }]} />
  </fieldset>

  const message = <>
    {restore.error && <span role="alert" className="text-sm text-alarm">{t(complaint(restore.error))}</span>}
    {count !== null && <span role="status" className="text-sm text-muted">{
      count === 0 ? t("routing.defaultsPresent") : t("routing.defaultsRestored", { count: String(count) })
    }</span>}
  </>

  return head ? <>
    <HeadActions>{menu}</HeadActions>
    {(restore.error || count !== null) && <div className="text-right">{message}</div>}
  </> : <div className="flex flex-wrap items-center justify-end gap-2">{message}{menu}</div>
}
