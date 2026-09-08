import { useState } from "react"
import { changePassword, complaint } from "@/api/auth"
import { Modal } from "@/components/Modal"
import { field, label, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import type { TextKey } from "@/i18n"
import { useAppDispatch } from "@/store/hooks"
import { sessionOpened } from "@/store/authSlice"

export function PasswordDialog({ onClose }: { onClose: () => void }) {
  const t = useText()
  const dispatch = useAppDispatch()
  const [current, setCurrent] = useState("")
  const [next, setNext] = useState("")
  const [again, setAgain] = useState("")
  const [error, setError] = useState<TextKey | null>(null)
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  const mismatch = again.length > 0 && next !== again
  const ready = current.length > 0 && next.length > 0 && !mismatch

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
    <Modal
      title={t("password.title")}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondary}>
            {t("password.cancel")}
          </button>
          <button type="button" disabled={busy || !ready} onClick={() => void submit()} className={primary}>
            {busy ? t("password.busy") : t("password.submit")}
          </button>
        </>
      }
    >
      <div>
        <label className={label} htmlFor="own-current">
          {t("password.current")}
        </label>
        <input
          id="own-current"
          type="password"
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
          autoComplete="current-password"
          className={`mt-1 ${field}`}
        />
      </div>

      <div>
        <label className={label} htmlFor="own-next">
          {t("password.next")}
        </label>
        <input
          id="own-next"
          type="password"
          value={next}
          onChange={(e) => setNext(e.target.value)}
          autoComplete="new-password"
          className={`mt-1 ${field}`}
        />
      </div>

      <div>
        <label className={label} htmlFor="own-again">
          {t("password.again")}
        </label>
        <input
          id="own-again"
          type="password"
          value={again}
          onChange={(e) => setAgain(e.target.value)}
          autoComplete="new-password"
          className={`mt-1 ${field}`}
        />
      </div>

      {mismatch && <div className="text-sm text-alarm">{t("password.mismatch")}</div>}
      {error && <div className="rounded bg-alarm-soft px-3 py-2 text-sm text-alarm">{t(error)}</div>}
      {done && <div className="text-sm text-brand-ink">{t("password.done")}</div>}
    </Modal>
  )
}
