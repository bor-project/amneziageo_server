import { useState } from "react"
import { complaint } from "@/api/auth"
import { useRoles } from "@/api/roles"
import { fresh, useAddUser, useChangeUser, useRemoveUser, useSetPassword, useUsers } from "@/api/users"
import type { User } from "@/api/users"
import { Modal } from "@/components/Modal"
import { RowActions } from "@/components/RowActions"
import { card, danger, field, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"

export function Users() {
  const t = useText()
  const users = useUsers(true)
  const catalog = useRoles()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<User | null>(null)
  const [keying, setKeying] = useState<User | null>(null)
  const [removing, setRemoving] = useState<User | null>(null)

  return (
    <div className={`mt-4 ${card}`}>
      <div className="flex items-center justify-between border-b border-line px-4 py-3">
        <span className="text-sm font-medium text-ink">{t("users.title")}</span>
        <button type="button" onClick={() => setAdding(true)} className={primary}>
          {t("users.add")}
        </button>
      </div>

      {users.data?.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("users.empty")}</div>}

      {users.data && users.data.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-xs text-muted">
              <tr>
                <th className="px-4 py-2 font-normal">{t("users.name")}</th>
                <th className="px-4 py-2 font-normal">{t("users.kind")}</th>
                <th className="px-4 py-2 font-normal">{t("users.role")}</th>
                <th className="px-4 py-2 font-normal">{t("users.state")}</th>
                <th className="px-4 py-2" />
              </tr>
            </thead>
            <tbody>
              {users.data.map((user) => (
                <tr key={user.name} className="border-t border-line">
                  <td className="px-4 py-2">
                    <div className="font-medium text-ink">{user.name}</div>
                    {user.displayName !== user.name && (
                      <div className="text-xs text-muted">{user.displayName}</div>
                    )}
                  </td>
                  <td className="px-4 py-2 text-muted">{t(`users.kind.${user.kind}` as TextKey)}</td>
                  <td className="px-4 py-2 text-muted">
                    {catalog.data?.roles.find((one) => one.name === user.role)?.title ??
                      (user.role.length > 0 ? user.role : t("role.none"))}
                  </td>
                  <td className="px-4 py-2">
                    <span className={user.enabled ? "text-brand-ink" : "text-alarm"}>
                      {t(user.enabled ? "users.enabled" : "users.disabled")}
                    </span>
                  </td>
                  <td className="px-4 py-2">
                    <RowActions
                      title={t("users.actions")}
                      actions={[
                        { label: t("users.edit"), onPick: () => setEditing(user) },
                        { label: t("users.password"), onPick: () => setKeying(user) },
                        { label: t("users.remove"), onPick: () => setRemoving(user), alarming: true },
                      ]}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {adding && <AddDialog onClose={() => setAdding(false)} />}
      {editing && <EditDialog user={editing} onClose={() => setEditing(null)} />}
      {keying && <PasswordDialog user={keying} onClose={() => setKeying(null)} />}
      {removing && <RemoveDialog user={removing} onClose={() => setRemoving(null)} />}
    </div>
  )
}

function RoleSelect({ id, value, onChange }: { id: string; value: string; onChange: (name: string) => void }) {
  const t = useText()
  const catalog = useRoles()

  return (
    <select id={id} value={value} onChange={(e) => onChange(e.target.value)} className={`mt-1 ${field}`}>
      <option value="">{t("role.none")}</option>
      {catalog.data?.roles.map((one) => (
        <option key={one.name} value={one.name}>
          {one.title.length > 0 ? one.title : one.name}
        </option>
      ))}
    </select>
  )
}

function AddDialog({ onClose }: { onClose: () => void }) {
  const t = useText()
  const add = useAddUser()
  const [name, setName] = useState("")
  const [displayName, setDisplayName] = useState("")
  const [role, setRole] = useState("")
  const [password, setPassword] = useState("")
  const [mustChange, setMustChange] = useState(true)
  const [host, setHost] = useState(false)
  const [publicKey, setPublicKey] = useState("")

  async function save() {
    await add.mutateAsync({
      name: name.trim(),
      displayName: displayName.trim(),
      role,
      password,
      mustChangePassword: mustChange,
      host,
      publicKey: publicKey.trim(),
    })
    onClose()
  }

  return (
    <Modal
      title={t("users.newTitle")}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("users.cancel")}
          </button>
          <button
            type="button"
            onClick={() => void save()}
            disabled={add.isPending || name.length === 0 || password.length === 0}
            className={primary}
          >
            {add.isPending ? t("users.busy") : t("users.save")}
          </button>
        </>
      }
    >
      <div>
        <label className={label} htmlFor="new-name">
          {t("users.name")}
        </label>
        <input id="new-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus className={`mt-1 ${field}`} />
      </div>

      <div>
        <label className={label} htmlFor="new-display">
          {t("users.displayName")}
        </label>
        <input
          id="new-display"
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          className={`mt-1 ${field}`}
        />
      </div>

      <div>
        <label className={label} htmlFor="new-role">
          {t("users.role")}
        </label>
        <RoleSelect id="new-role" value={role} onChange={setRole} />
      </div>

      <Secret
        id="new-password"
        value={password}
        onChange={setPassword}
        mustChange={mustChange}
        onMustChange={setMustChange}
      />

      <label className="flex items-center gap-2 text-sm text-muted">
        <input type="checkbox" checked={host} onChange={(e) => setHost(e.target.checked)} />
        {t("users.host")}
      </label>

      {host && (
        <div>
          <label className={label} htmlFor="new-key">
            {t("users.key")}
          </label>
          <textarea
            id="new-key"
            rows={3}
            value={publicKey}
            onChange={(e) => setPublicKey(e.target.value)}
            className={`mt-1 ${field}`}
          />
        </div>
      )}

      <Complaint error={add.error} />
    </Modal>
  )
}

function EditDialog({ user, onClose }: { user: User; onClose: () => void }) {
  const t = useText()
  const change = useChangeUser()
  const [role, setRole] = useState(user.role)
  const [enabled, setEnabled] = useState(user.enabled)
  const [publicKey, setPublicKey] = useState("")

  async function save() {
    const key = publicKey.trim()
    await change.mutateAsync({
      name: user.name,
      change: key.length > 0 ? { role, enabled, publicKey: key } : { role, enabled },
    })
    onClose()
  }

  return (
    <Modal
      title={t("users.editTitle", { name: user.name })}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("users.cancel")}
          </button>
          <button type="button" onClick={() => void save()} disabled={change.isPending} className={primary}>
            {change.isPending ? t("users.busy") : t("users.save")}
          </button>
        </>
      }
    >
      <div>
        <label className={label} htmlFor="edit-role">
          {t("users.role")}
        </label>
        <RoleSelect id="edit-role" value={role} onChange={setRole} />
      </div>

      <label className="flex items-center gap-2 text-sm text-muted">
        <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
        {t("users.on")}
      </label>

      {user.hostUser.length > 0 && (
        <div>
          <label className={label} htmlFor="edit-key">
            {t("users.key")}
          </label>
          <textarea
            id="edit-key"
            rows={3}
            value={publicKey}
            onChange={(e) => setPublicKey(e.target.value)}
            placeholder={user.hasKey ? t("users.keyHeld") : ""}
            className={`mt-1 ${field}`}
          />
        </div>
      )}

      <Complaint error={change.error} />
    </Modal>
  )
}

function PasswordDialog({ user, onClose }: { user: User; onClose: () => void }) {
  const t = useText()
  const set = useSetPassword()
  const [password, setPassword] = useState("")
  const [mustChange, setMustChange] = useState(true)

  async function save() {
    await set.mutateAsync({ name: user.name, password, mustChangePassword: mustChange })
    onClose()
  }

  return (
    <Modal
      title={t("users.passwordTitle", { name: user.name })}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("users.cancel")}
          </button>
          <button
            type="button"
            onClick={() => void save()}
            disabled={set.isPending || password.length === 0}
            className={primary}
          >
            {set.isPending ? t("users.busy") : t("users.save")}
          </button>
        </>
      }
    >
      <Secret
        id="set-password"
        value={password}
        onChange={setPassword}
        mustChange={mustChange}
        onMustChange={setMustChange}
      />

      <Complaint error={set.error} />
    </Modal>
  )
}

function RemoveDialog({ user, onClose }: { user: User; onClose: () => void }) {
  const t = useText()
  const remove = useRemoveUser()

  async function drop() {
    await remove.mutateAsync(user.name)
    onClose()
  }

  return (
    <Modal
      title={t("users.removeTitle", { name: user.name })}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("users.cancel")}
          </button>
          <button type="button" onClick={() => void drop()} disabled={remove.isPending} className={danger}>
            {remove.isPending ? t("users.busy") : t("users.remove")}
          </button>
        </>
      }
    >
      <Complaint error={remove.error} />
    </Modal>
  )
}

function Secret({
  id,
  value,
  onChange,
  mustChange,
  onMustChange,
}: {
  id: string
  value: string
  onChange: (value: string) => void
  mustChange: boolean
  onMustChange: (value: boolean) => void
}) {
  const t = useText()

  return (
    <div>
      <label className={label} htmlFor={id}>
        {t("users.password")}
      </label>
      <div className="mt-1 flex gap-2">
        <input id={id} value={value} onChange={(e) => onChange(e.target.value)} className={field} />
        <button type="button" onClick={() => onChange(fresh())} className={secondary}>
          {t("users.generate")}
        </button>
      </div>
      <label className="mt-2 flex items-center gap-2 text-sm text-muted">
        <input type="checkbox" checked={mustChange} onChange={(e) => onMustChange(e.target.checked)} />
        {t("users.mustChange")}
      </label>
    </div>
  )
}

function Complaint({ error }: { error: unknown }) {
  const t = useText()

  if (!error) {
    return null
  }

  return <div className="rounded bg-alarm-soft px-3 py-2 text-sm text-alarm">{t(complaint(error))}</div>
}
