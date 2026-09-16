import { useState } from "react"
import { Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { useRoles } from "@/api/roles"
import { fresh, useAddUser, useChangeUser, useUsers } from "@/api/users"
import { useTail } from "@/components/crumbs"
import { Flag, Line, Part, Pick } from "@/components/fields"
import { card, field, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function UserPage() {
  const { name } = useParams()

  return name === undefined ? <NewUser /> : <HeldUser name={name} />
}

function NewUser() {
  const t = useText()
  const navigate = useNavigate()
  const add = useAddUser()
  const catalog = useRoles()
  const [name, setName] = useState("")
  const [displayName, setDisplayName] = useState("")
  const [role, setRole] = useState("")
  const [password, setPassword] = useState("")
  const [mustChange, setMustChange] = useState(true)
  const [host, setHost] = useState(false)
  const [publicKey, setPublicKey] = useState("")

  useTail([{ label: t("users.newTitle") }])

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
    navigate("/settings/users")
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("users.partMain")}>
        <Line id="new-name" caption={t("users.name")} value={name} onChange={setName} />

        <Line id="new-display" caption={t("users.displayName")} value={displayName} onChange={setDisplayName} />

        <Pick id="new-role" caption={t("users.role")} value={role} onChange={setRole}>
          <option value="">{t("role.none")}</option>
          {(catalog.data?.roles ?? []).map((one) => (
            <option key={one.name} value={one.name}>
              {one.title.length > 0 ? one.title : one.name}
            </option>
          ))}
        </Pick>

        <div />

        <Secret id="new-password" value={password} onChange={setPassword} />

        <div className="flex flex-wrap items-center gap-6 sm:col-span-2">
          <Flag id="new-must-change" caption={t("users.mustChange")} value={mustChange} onChange={setMustChange} />
          <Flag id="new-host" caption={t("users.host")} value={host} onChange={setHost} />
        </div>

        {host && (
          <div className="sm:col-span-2">
            <label className={label} htmlFor="new-key">
              {t("users.key")}
            </label>
            <textarea
              id="new-key"
              rows={3}
              value={publicKey}
              onChange={(e) => setPublicKey(e.target.value)}
              className={`mt-1 font-mono text-xs ${field}`}
            />
          </div>
        )}
      </Part>

      {add.error !== null && <div className="text-sm text-alarm">{t(complaint(add.error))}</div>}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        <button type="button" onClick={() => navigate("/settings/users")} className={secondary}>
          {t("users.cancel")}
        </button>
        <button
          type="button"
          onClick={() => void save()}
          disabled={add.isPending || name.trim().length === 0 || password.length === 0}
          className={primary}
        >
          {add.isPending ? t("users.busy") : t("users.save")}
        </button>
      </div>
    </div>
  )
}

function HeldUser({ name }: { name: string }) {
  const t = useText()
  const navigate = useNavigate()
  const users = useUsers(true)
  const catalog = useRoles()
  const change = useChangeUser()
  const held = (users.data ?? []).find((one) => one.name === name)
  const [role, setRole] = useState<string | null>(null)
  const [enabled, setEnabled] = useState<boolean | null>(null)
  const [publicKey, setPublicKey] = useState("")

  useTail([{ label: name }, { label: t("users.edit") }])

  if (held === undefined) {
    return users.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("users.loading")}</div>
    ) : (
      <Navigate to="/settings/users" replace />
    )
  }

  const picked = role ?? held.role
  const on = enabled ?? held.enabled

  async function save() {
    const key = publicKey.trim()
    await change.mutateAsync({
      name,
      change: key.length > 0 ? { role: picked, enabled: on, publicKey: key } : { role: picked, enabled: on },
    })
    navigate("/settings/users")
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("users.partMain")}>
        <Pick id="edit-role" caption={t("users.role")} value={picked} onChange={setRole}>
          <option value="">{t("role.none")}</option>
          {(catalog.data?.roles ?? []).map((one) => (
            <option key={one.name} value={one.name}>
              {one.title.length > 0 ? one.title : one.name}
            </option>
          ))}
        </Pick>

        <div />

        <div className="sm:col-span-2">
          <Flag id="edit-enabled" caption={t("users.on")} value={on} onChange={setEnabled} />
        </div>

        {held.hostUser.length > 0 && (
          <div className="sm:col-span-2">
            <label className={label} htmlFor="edit-key">
              {t("users.key")}
            </label>
            <textarea
              id="edit-key"
              rows={3}
              value={publicKey}
              placeholder={held.hasKey ? t("users.keyHeld") : ""}
              onChange={(e) => setPublicKey(e.target.value)}
              className={`mt-1 font-mono text-xs ${field}`}
            />
          </div>
        )}
      </Part>

      {change.error !== null && <div className="text-sm text-alarm">{t(complaint(change.error))}</div>}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        <button type="button" onClick={() => navigate("/settings/users")} className={secondary}>
          {t("users.cancel")}
        </button>
        <button type="button" onClick={() => void save()} disabled={change.isPending} className={primary}>
          {change.isPending ? t("users.busy") : t("users.save")}
        </button>
      </div>
    </div>
  )
}

function Secret({ id, value, onChange }: { id: string; value: string; onChange: (value: string) => void }) {
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
    </div>
  )
}
