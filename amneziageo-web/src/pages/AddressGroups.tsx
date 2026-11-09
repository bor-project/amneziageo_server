import { useRoutingEditor } from "@/components/useRoutingEditor"
import { useState } from "react"
import { complaint } from "@/api/auth"
import { useAddAddressGroup, useAddressGroups, useChangeAddressGroup, useRemoveAddressGroup } from "@/api/addressGroups"
import type { AddressGroup, AddressGroupDraft } from "@/api/addressGroups"
import { useFailure } from "@/api/failure"
import { scopes } from "@/api/scopes"
import { Find, Line, ListArea } from "@/components/fields"
import { RoutingEditor, RoutingSettings, RoutingToolbar } from "@/components/RoutingEditor"
import { Rows } from "@/components/Rows"
import { alarmLine, card, danger, footerUnder, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { same } from "@/store/draftSlice"
import { useAppSelector } from "@/store/hooks"

export function AddressGroups() {
  const t = useText()
  const user = useAppSelector((state) => state.auth.user)
  const editor = useRoutingEditor()
  const groups = useAddressGroups()
  const failed = useFailure(groups)
  const may = holds(user, scopes.manageRouting)
  const find = (editor.params.get("find") ?? "").toLowerCase()
  const shown = (groups.data ?? []).filter((group) => (group.name + " " + group.targets.join(" ")).toLowerCase().includes(find))
  const held = shown.find((group) => group.id === editor.id)
  const open = (group?: AddressGroup) => <GroupEditor key={group?.id ?? "new"} held={group} onClose={editor.close} />

  return (
    <>
      <RoutingToolbar onAdd={may && groups.isSuccess ? () => editor.open("new") : undefined}>
        <Find value={editor.params.get("find") ?? ""} onChange={(value) => editor.filter("find", value)} className="w-full wide:w-72" />
      </RoutingToolbar>
      <div className={card}>
        {failed !== null && <div className={alarmLine}>{t(complaint(failed))}</div>}
        {groups.isPending && <div className="p-4 text-muted">{t("rules.loading")}</div>}
        {groups.data !== undefined && <Rows name="routing-address-groups" items={shown} keyOf={(group) => group.id}
          creating={may && editor.fresh ? open() : undefined}
          expanded={may && held ? { key: held.id, body: open(held) } : undefined}
          columns={[
            { key: "name", caption: t("routing.addressGroup"), width: 200, lead: true,
              cell: (group) => may ? <button type="button" className="font-semibold text-ink hover:text-brand-ink" onClick={() => editor.open(group.id)}>{group.name}</button> : group.name },
            { key: "targets", caption: t("routing.contents"), wrap: true,
              cell: (group) => <span title={group.targets.join("\n")}>{group.targets.slice(0, 4).join(", ")}{group.targets.length > 4 ? " …" : ""}</span> },
            { key: "rules", caption: t("tab.rules"), width: 80, cell: (group) => group.rules.length },
            { key: "actions", caption: t("rules.actions"), width: 56, tail: true,
              cell: (group) => may && <div className="flex justify-end"><RoutingSettings onClick={() => editor.open(group.id)} open={held?.id === group.id} /></div> },
          ]}
        />}
      </div>
    </>
  )
}

function GroupEditor({ held, onClose }: { held?: AddressGroup; onClose: () => void }) {
  const t = useText()
  const start = { name: held?.name ?? "", targets: held?.targets ?? [] }
  const [draft, setDraft] = useState<AddressGroupDraft>(start)
  const [removing, setRemoving] = useState(false)
  const add = useAddAddressGroup()
  const change = useChangeAddressGroup()
  const remove = useRemoveAddressGroup()
  const pending = add.isPending || change.isPending || remove.isPending
  const error = add.error ?? change.error ?? remove.error

  return (
    <RoutingEditor title={t("routing.addressGroup")} onClose={onClose}>
      {removing && held ? <div className="mt-4">
        <h3 className="text-base text-ink">{t("rules.removeTitle", { name: held.name })}</h3>
        <div className={footerUnder + " routing-form-footer"}>
          <button type="button" className={secondary} disabled={pending} onClick={() => setRemoving(false)}>{t("rules.cancel")}</button>
          <button type="button" className={danger} disabled={pending} onClick={() => void remove.mutateAsync(held.id).then(onClose).catch(() => undefined)}>{t("rules.remove")}</button>
        </div>
      </div> : <>
        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          <Line id="address-group-name" caption={t("rules.name")} value={draft.name} onChange={(name) => setDraft({ ...draft, name })} />
          <div className="sm:col-span-2">
            <ListArea id="address-group-targets" caption={t("routing.addresses")} value={draft.targets} onChange={(targets) => setDraft({ ...draft, targets })} />
          </div>
        </div>
        <div className={footerUnder + " routing-form-footer"}>
          {held && <button type="button" className={"mr-auto " + danger} disabled={pending || held.rules.length > 0}
            title={held.rules.length > 0 ? t("routing.groupInUse") : undefined} onClick={() => setRemoving(true)}>{t("rules.remove")}</button>}
          <button type="button" className={secondary} disabled={pending} onClick={onClose}>{t("rules.cancel")}</button>
          <button type="button" className={primary} disabled={pending || same(draft, start) || draft.name.trim() === "" || draft.targets.length === 0}
            onClick={() => void (held ? change.mutateAsync({ id: held.id, draft }) : add.mutateAsync(draft)).then(onClose).catch(() => undefined)}>{t("rules.save")}</button>
        </div>
      </>}
      {error && <div role="alert" className="mt-2 text-sm text-alarm">{t(complaint(error))}</div>}
    </RoutingEditor>
  )
}
