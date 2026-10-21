import { useState } from "react"
import { Navigate, useNavigate, useParams } from "react-router-dom"
import { useApiTokens } from "@/api/apiTokens"
import { complaint } from "@/api/auth"
import { useAddRole, useChangeRole, useRoles } from "@/api/roles"
import { useUsers } from "@/api/users"
import { useTail } from "@/components/crumbs"
import { Line, LinkLine, Links, Part } from "@/components/fields"
import { tokenItems, userItems } from "@/components/members"
import { danger, footer, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { same } from "@/store/draftSlice"
import { useSpot } from "@/store/spots"

export function RolePage() {
  const { name } = useParams()

  return name === undefined ? <NewRole /> : <HeldRole name={name} />
}

function NewRole() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/settings/users")
  const catalog = useRoles()
  const add = useAddRole()
  const [name, setName] = useState("")
  const [title, setTitle] = useState("")
  const [held, setHeld] = useState<string[]>([])
  const users = useUsers(true).data ?? []
  const [members, setMembers] = useState<string[]>([])
  const edited = name.length > 0 || title.length > 0 || held.length > 0 || members.length > 0

  useTail([{ label: t("roles.newTitle") }])

  async function save() {
    await add.mutateAsync({
      name: name.trim(),
      title: title.trim(),
      scopes: held,
      ...(members.length > 0 ? { users: members } : {}),
    })
    navigate(back)
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("roles.partMain")}>
        <Line id="new-role-name" caption={t("roles.name")} value={name} onChange={setName} />
        <Line id="new-role-title" caption={t("roles.label")} value={title} onChange={setTitle} />
        <Rights all={catalog.data?.scopes ?? []} held={held} onChange={setHeld} />
      </Part>

      <Part title={t("roles.users")}>
        <div className="sm:col-span-2">
          <Links
            id="new-role-users"
            value={members}
            items={userItems(users, catalog.data?.roles ?? [], t("role.none"))}
            placeholder={t("roles.addUser")}
            onChange={setMembers}
          />
        </div>
      </Part>

      {add.error !== null && <div className="text-sm text-alarm">{t(complaint(add.error))}</div>}

      <div className={footer}>
        <button type="button" onClick={() => navigate(back)} disabled={!edited || add.isPending} className={secondary}>
          {t("roles.cancel")}
        </button>
        <button
          type="button"
          onClick={() => void save()}
          disabled={add.isPending || name.trim().length === 0}
          className={primary}
        >
          {add.isPending ? t("roles.busy") : t("roles.save")}
        </button>
      </div>
    </div>
  )
}

function HeldRole({ name }: { name: string }) {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/settings/users")
  const catalog = useRoles()
  const change = useChangeRole()
  const held = catalog.data?.roles.find((one) => one.name === name)
  const [title, setTitle] = useState<string | null>(null)
  const [scopes, setScopes] = useState<string[] | null>(null)
  const users = useUsers(true).data ?? []
  const tokens = useApiTokens().data ?? []
  const [members, setMembers] = useState<string[] | null>(null)

  useTail([{ label: name }])

  if (held === undefined) {
    return catalog.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("roles.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  const shown = title ?? held.title
  const rights = scopes ?? held.scopes
  const builtin = held.builtin
  const holding = users.filter((one) => one.role === name).map((one) => one.name)
  const team = members ?? holding
  const mine = tokens.filter((one) => one.role === name)
  const edited =
    shown.trim() !== held.title ||
    (!builtin && !same(rights, held.scopes)) ||
    (members !== null && !same(members, holding))

  async function save() {
    await change.mutateAsync({
      name,
      change: {
        ...(builtin ? { title: shown.trim() } : { title: shown.trim(), scopes: rights }),
        ...(members === null ? {} : { users: members }),
      },
    })
    navigate(back)
  }

  return (
    <div className="mt-4 flex flex-col gap-4">
      <Part title={t("roles.partMain")}>
        <Line id="edit-role-title" caption={t("roles.label")} value={shown} onChange={setTitle} />
        <div />
        <Rights all={catalog.data?.scopes ?? []} held={rights} onChange={setScopes} locked={builtin} />
      </Part>

      <Part title={t("roles.users")}>
        <div className="sm:col-span-2">
          <Links
            id="role-users"
            value={team}
            items={userItems(users, catalog.data?.roles ?? [], t("role.none"))}
            placeholder={t("roles.addUser")}
            fixed={holding}
            onChange={setMembers}
          />
        </div>
      </Part>

      <Part title={t("roles.tokens")}>
        <div className="text-sm text-ink sm:col-span-2">
          {mine.length === 0 ? t("clients.dash") : <LinkLine items={tokenItems(mine)} />}
        </div>
      </Part>

      {change.error !== null && <div className="text-sm text-alarm">{t(complaint(change.error))}</div>}

      <div className={footer}>
        {!builtin && (
          <button
            type="button"
            onClick={() => navigate(`/settings/users/roles/${name}/delete`)}
            className={`mr-auto ${danger}`}
          >
            {t("roles.remove")}
          </button>
        )}
        <button
          type="button"
          onClick={() => navigate(back)}
          disabled={!edited || change.isPending}
          className={secondary}
        >
          {t("roles.cancel")}
        </button>
        <button
          type="button"
          onClick={() => void save()}
          disabled={!edited || change.isPending}
          className={primary}
        >
          {change.isPending ? t("roles.busy") : t("roles.save")}
        </button>
      </div>
    </div>
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
    <div className="sm:col-span-2">
      <span className={label}>{t("roles.rights")}</span>
      <div className="mt-1 flex flex-col gap-2">
        {all.map((scope) => (
          <label key={scope} className="flex items-center gap-2 text-sm text-muted" htmlFor={`scope-${scope}`}>
            <input
              id={`scope-${scope}`}
              type="checkbox"
              checked={held.includes(scope)}
              disabled={locked}
              onChange={(e) => toggle(scope, e.target.checked)}
              className="size-4 accent-brand"
            />
            {t(`scope.${scope}` as TextKey)}
          </label>
        ))}
      </div>
    </div>
  )
}
