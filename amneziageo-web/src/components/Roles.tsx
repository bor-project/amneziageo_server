import { useState } from "react"
import { complaint } from "@/api/auth"
import { useAddRole, useChangeRole, useRemoveRole, useRoles } from "@/api/roles"
import type { Role } from "@/api/roles"
import { Modal } from "@/components/Modal"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, danger, field, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

export function Roles() {
  const t = useText()
  const catalog = useRoles()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<Role | null>(null)
  const [removing, setRemoving] = useState<Role | null>(null)

  return (
    <div className={`mt-4 ${card}`}>
      <div className="flex items-center justify-between border-b border-line px-4 py-3">
        <span className="text-sm font-medium text-ink">{t("roles.title")}</span>
        <button type="button" onClick={() => setAdding(true)} className={primary}>
          {t("roles.add")}
        </button>
      </div>

      {catalog.data?.roles.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("roles.empty")}</div>}

      {catalog.data && catalog.data.roles.length > 0 && (
        <Rows
          items={catalog.data.roles}
          keyOf={(role) => role.name}
          columns={[
            {
              key: "title",
              caption: t("roles.label"),
              sort: (role) => (role.title.length > 0 ? role.title : role.name),
              lead: true,
              cell: (role) => (
                <>
                  <div className="font-medium text-ink">{role.title.length > 0 ? role.title : role.name}</div>
                  <div className="text-xs text-muted">
                    {role.name}
                    {role.builtin ? ` · ${t("roles.builtin")}` : ""}
                  </div>
                </>
              ),
            },
            {
              key: "rights",
              caption: t("roles.rights"),
              sort: (role) =>
                role.scopes.length === 0
                  ? t("roles.none")
                  : role.scopes.map((scope) => t(`scope.${scope}` as TextKey)).join(", "),
              body: "text-muted",
              cell: (role) =>
                role.scopes.length === 0
                  ? t("roles.none")
                  : role.scopes.map((scope) => t(`scope.${scope}` as TextKey)).join(", "),
            },
            {
              key: "users",
              caption: t("roles.users"),
              sort: (role) => role.users,
              body: "text-muted",
              cell: (role) => role.users,
            },
            {
              key: "actions",
              caption: t("roles.actions"),
              tail: true,
              cell: (role) => (
                <RowActions
                  title={t("roles.actions")}
                  actions={[
                    { label: t("roles.edit"), onPick: () => setEditing(role) },
                    { label: t("roles.remove"), onPick: () => setRemoving(role), alarming: true },
                  ]}
                />
              ),
            },
          ]}
        />
      )}

      {adding && <AddDialog scopes={catalog.data?.scopes ?? []} onClose={() => setAdding(false)} />}
      {editing && <EditDialog role={editing} scopes={catalog.data?.scopes ?? []} onClose={() => setEditing(null)} />}
      {removing && <RemoveDialog role={removing} onClose={() => setRemoving(null)} />}
    </div>
  )
}

function AddDialog({ scopes, onClose }: { scopes: string[]; onClose: () => void }) {
  const t = useText()
  const add = useAddRole()
  const [name, setName] = useState("")
  const [title, setTitle] = useState("")
  const [held, setHeld] = useState<string[]>([])

  async function save() {
    await add.mutateAsync({ name: name.trim(), title: title.trim(), scopes: held })
    onClose()
  }

  return (
    <Modal
      title={t("roles.newTitle")}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("roles.cancel")}
          </button>
          <button type="button" onClick={() => void save()} disabled={add.isPending || name.length === 0} className={primary}>
            {add.isPending ? t("roles.busy") : t("roles.save")}
          </button>
        </>
      }
    >
      <div>
        <label className={label} htmlFor="new-role-name">
          {t("roles.name")}
        </label>
        <input id="new-role-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus className={`mt-1 ${field}`} />
      </div>

      <div>
        <label className={label} htmlFor="new-role-title">
          {t("roles.label")}
        </label>
        <input id="new-role-title" value={title} onChange={(e) => setTitle(e.target.value)} className={`mt-1 ${field}`} />
      </div>

      <Rights all={scopes} held={held} onChange={setHeld} />

      <Complaint error={add.error} />
    </Modal>
  )
}

function EditDialog({ role, scopes, onClose }: { role: Role; scopes: string[]; onClose: () => void }) {
  const t = useText()
  const change = useChangeRole()
  const [title, setTitle] = useState(role.title)
  const [held, setHeld] = useState<string[]>(role.scopes)

  async function save() {
    await change.mutateAsync({
      name: role.name,
      change: role.builtin ? { title: title.trim() } : { title: title.trim(), scopes: held },
    })

    onClose()
  }

  return (
    <Modal
      title={t("roles.editTitle", { name: role.name })}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("roles.cancel")}
          </button>
          <button type="button" onClick={() => void save()} disabled={change.isPending} className={primary}>
            {change.isPending ? t("roles.busy") : t("roles.save")}
          </button>
        </>
      }
    >
      <div>
        <label className={label} htmlFor="edit-role-title">
          {t("roles.label")}
        </label>
        <input id="edit-role-title" value={title} onChange={(e) => setTitle(e.target.value)} autoFocus className={`mt-1 ${field}`} />
      </div>

      <Rights all={scopes} held={held} onChange={setHeld} locked={role.builtin} />

      <Complaint error={change.error} />
    </Modal>
  )
}

function RemoveDialog({ role, onClose }: { role: Role; onClose: () => void }) {
  const t = useText()
  const remove = useRemoveRole()

  async function drop() {
    await remove.mutateAsync(role.name)
    onClose()
  }

  return (
    <Modal
      title={t("roles.removeTitle", { name: role.name })}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("roles.cancel")}
          </button>
          <button type="button" onClick={() => void drop()} disabled={remove.isPending} className={danger}>
            {remove.isPending ? t("roles.busy") : t("roles.remove")}
          </button>
        </>
      }
    >
      <Complaint error={remove.error} />
    </Modal>
  )
}

function Rights({
  all,
  held,
  onChange,
  locked = false,
}: {
  all: string[]
  held: string[]
  onChange: (scopes: string[]) => void
  locked?: boolean
}) {
  const t = useText()

  function toggle(scope: string, on: boolean) {
    onChange(on ? [...held, scope] : held.filter((one) => one !== scope))
  }

  return (
    <div>
      <span className={label}>{t("roles.rights")}</span>
      <div className="mt-1 flex flex-col gap-2">
        {all.map((scope) => (
          <label key={scope} className="flex items-center gap-2 text-sm text-muted">
            <input
              type="checkbox"
              checked={held.includes(scope)}
              disabled={locked}
              onChange={(e) => toggle(scope, e.target.checked)}
            />
            {t(`scope.${scope}` as TextKey)}
          </label>
        ))}
      </div>
    </div>
  )
}

function Complaint({ error }: { error: unknown }) {
  const t = useText()

  return error ? <p className="text-sm text-alarm">{t(complaint(error))}</p> : null
}
