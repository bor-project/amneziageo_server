import { useEffect } from "react"
import type { ReactNode } from "react"
import { whoAmI } from "@/api/auth"
import { renew } from "@/api/client"
import { refresh } from "@/api/tokens"
import { useText } from "@/i18n"
import { useAppDispatch, useAppSelector } from "@/store/hooks"
import { sessionClosed, sessionRestored } from "@/store/authSlice"

export function Boot({ children }: { children: ReactNode }) {
  const t = useText()
  const dispatch = useAppDispatch()
  const status = useAppSelector((s) => s.auth.status)

  useEffect(() => {
    let alive = true

    async function restore() {
      if (!refresh() || !(await renew())) {
        dispatch(sessionClosed())
        return
      }

      try {
        const account = await whoAmI()
        if (alive) {
          dispatch(sessionRestored(account))
        }
      } catch {
        dispatch(sessionClosed())
      }
    }

    void restore()

    return () => {
      alive = false
    }
  }, [dispatch])

  if (status === "unknown") {
    return (
      <div className="flex h-full items-center justify-center bg-canvas text-sm text-muted">
        {t("boot.loading")}
      </div>
    )
  }

  return <>{children}</>
}
