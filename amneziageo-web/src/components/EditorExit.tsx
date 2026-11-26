import { useEffect, useRef } from "react"
import { useBlocker } from "react-router-dom"
import { Dialog } from "@/components/Dialog"
import { danger, secondary } from "@/components/styles"
import { useText } from "@/i18n"

export function useEditorExit(dirty: boolean, pending: boolean) {
  const t = useText()
  const committed = useRef(false)
  const blocker = useBlocker(({ currentLocation, nextLocation }) =>
    !committed.current && (dirty || pending) && currentLocation.pathname !== nextLocation.pathname)

  useEffect(() => {
    if (!dirty && !pending) {
      return
    }

    const unload = (event: BeforeUnloadEvent) => {
      if (!committed.current) {
        event.preventDefault()
        event.returnValue = ""
      }
    }

    window.addEventListener("beforeunload", unload)
    return () => window.removeEventListener("beforeunload", unload)
  }, [dirty, pending])

  function leave(action: () => void) {
    committed.current = true
    if (blocker.state === "blocked") {
      blocker.reset()
    }
    action()
  }

  const prompt = blocker.state === "blocked" && (
    <Dialog
      title={t("editor.unsaved")}
      onClose={() => blocker.reset()}
      actions={<>
        <button type="button" className={secondary} onClick={() => blocker.reset()}>{t("editor.stay")}</button>
        <button type="button" className={danger} disabled={pending} onClick={() => blocker.proceed()}>{t("editor.discard")}</button>
      </>}
    />
  )

  return { leave, prompt }
}
