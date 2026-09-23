import { useState } from "react"
import { useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { fresh, useSetPassword } from "@/api/users"
import { useTail } from "@/components/crumbs"
import { Flag, Part } from "@/components/fields"
import { card, field, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useSpot } from "@/store/spots"

export function UserPassword() {
  const t = useText()
  const navigate = useNavigate()
  const back = useSpot("/settings/users")
  const { name = "" } = useParams()
  const set = useSetPassword()
  const [password, setPassword] = useState("")
  const [mustChange, setMustChange] = useState(true)

  useTail([{ label: name, to: `/settings/users/${name}/edit` }, { label: t("users.password") }])

  async function save() {
    await set.mutateAsync({ name, password, mustChangePassword: mustChange })
    navigate(back)
  }

  return (
    <div className="mt-4 flex max-w-[42rem] flex-col gap-4">
      <Part title={t("users.passwordTitle", { name })}>
        <div className="sm:col-span-2">
          <label className={label} htmlFor="set-password">
            {t("users.password")}
          </label>
          <div className="mt-1 flex gap-2">
            <input
              id="set-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className={field}
            />
            <button type="button" onClick={() => setPassword(fresh())} className={secondary}>
              {t("users.generate")}
            </button>
          </div>
        </div>

        <div className="sm:col-span-2">
          <Flag id="set-must-change" caption={t("users.mustChange")} value={mustChange} onChange={setMustChange} />
        </div>
      </Part>

      {set.error !== null && <div className="text-sm text-alarm">{t(complaint(set.error))}</div>}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        <button type="button" onClick={() => navigate(back)} className={secondary}>
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
      </div>
    </div>
  )
}
