import { useState } from "react"
import { complaint } from "@/api/auth"
import type { Config } from "@/api/configs"
import { useInternetOnlyProfile } from "@/api/routingEntries"
import { Dialog } from "@/components/Dialog"
import { alarmLine, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function InternetOnlyProfile({ configs }: { configs: Config[] }) {
  const t = useText()
  const apply = useInternetOnlyProfile()
  const [open, setOpen] = useState(false)
  const [selected, setSelected] = useState<number[]>([])
  const [added, setAdded] = useState<boolean | null>(null)
  const ids = selected.filter((id) => configs.some((config) => config.id === id))

  function show() {
    apply.reset()
    setAdded(null)
    setSelected([])
    setOpen(true)
  }

  function close() {
    if (!apply.isPending) {
      setOpen(false)
    }
  }

  function save() {
    void apply.mutateAsync({
      interfaceIds: ids,
      name: t("routing.internetOnly"),
      addressesName: t("routing.privateNetworks"),
    }).then((result) => {
      setAdded(result.added)
      setOpen(false)
    }).catch(() => undefined)
  }

  return <>
    {added !== null && <span role="status" className="text-sm text-muted">
      {t(added ? "routing.profileAdded" : "routing.profilePresent")}
    </span>}
    <button type="button" className={secondary} onClick={show} disabled={configs.length === 0}
      title={configs.length === 0 ? t("routing.profileNoInterfaces") : undefined}>
      {t("routing.profiles")}
    </button>
    {open && <Dialog title={t("routing.internetOnly")} onClose={close} actions={<>
      <button type="button" className={secondary} disabled={apply.isPending} onClick={close}>{t("rules.cancel")}</button>
      <button type="button" className={primary} disabled={ids.length === 0 || apply.isPending} onClick={save}>{t("outbounds.apply")}</button>
    </>}>
      <fieldset className="mt-1 flex max-h-[50vh] flex-col gap-2 overflow-y-auto" disabled={apply.isPending}>
        <legend className="mb-2">{t("rules.interfaces")}</legend>
        {configs.map((config) => <label key={config.id} className="flex min-w-0 items-center gap-2 py-1 text-ink">
          <input type="checkbox" checked={ids.includes(config.id)}
            disabled={ids.length >= 64 && !ids.includes(config.id)}
            onChange={({ target }) => setSelected((held) => target.checked ? [...held, config.id] : held.filter((id) => id !== config.id))} />
          <span className="truncate">{config.name}</span>
        </label>)}
      </fieldset>
      {apply.error && <div role="alert" className={alarmLine}>{t(complaint(apply.error))}</div>}
    </Dialog>}
  </>
}
