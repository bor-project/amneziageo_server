import { useState } from "react"
import { useNavigate } from "react-router-dom"
import { changePassword, complaint } from "@/api/auth"
import { useCrumbs } from "@/components/crumbs"
import { Part } from "@/components/fields"
import { card, field, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { sessionOpened } from "@/store/authSlice"
import { useAppDispatch } from "@/store/hooks"

export function OwnPassword() {
  const t = useText()
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const [current, setCurrent] = useState("")
  const [next, setNext] = useState("")
  const [again, setAgain] = useState("")
  const [error, setError] = useState<TextKey | null>(null)
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)
  const mismatch = again.length > 0 && next !== again
  const ready = current.length > 0 && next.length > 0 && !mismatch

  useCrumbs([{ label: t("password.title") }])

  async function submit() {
    setBusy(true)
    setError(null)
    setDone(false)
    try {
      dispatch(sessionOpened(await changePassword(current, next)))
      setCurrent("")
      setNext("")
      setAgain("")
      setDone(true)
    } catch (failure) {
      setError(complaint(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex max-w-[35rem] flex-col gap-4">
      <h1 className="text-2xl leading-10 font-semibold tracking-[-0.02em]">{t("password.title")}</h1>

      <Part title={t("password.title")}>
        <Hidden id="own-current" caption={t("password.current")} value={current} onChange={setCurrent} own />
        <div />
        <Hidden id="own-next" caption={t("password.next")} value={next} onChange={setNext} />
        <Hidden id="own-again" caption={t("password.again")} value={again} onChange={setAgain} />
      </Part>

      {mismatch && <div className="text-sm text-alarm">{t("password.mismatch")}</div>}
      {error && <div className="text-sm text-alarm">{t(error)}</div>}
      {done && <div className="text-sm text-good">{t("password.done")}</div>}

      <div className={`flex justify-end gap-2 px-4 py-3.5 ${card}`}>
        <button type="button" onClick={() => navigate(-1)} className={secondary}>
          {t("password.cancel")}
        </button>
        <button type="button" disabled={busy || !ready} onClick={() => void submit()} className={primary}>
          {busy ? t("password.busy") : t("password.submit")}
        </button>
      </div>
    </div>
  )
}

function Hidden({
  id,
  caption,
  value,
  onChange,
  own = false,
}: {
  id: string
  caption: string
  value: string
  onChange: (value: string) => void
  own?: boolean
}) {
  return (
    <div>
      <label className={label} htmlFor={id}>
        {caption}
      </label>
      <input
        id={id}
        type="password"
        value={value}
        autoComplete={own ? "current-password" : "new-password"}
        onChange={(e) => onChange(e.target.value)}
        className={`mt-1 ${field}`}
      />
    </div>
  )
}
