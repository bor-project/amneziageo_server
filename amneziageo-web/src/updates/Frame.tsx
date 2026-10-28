import { Fragment, useContext, useEffect } from "react"
import { Link, Outlet, useLocation, useNavigate } from "react-router-dom"
import { queryClient } from "@/api/queryClient"
import { AppSign } from "@/components/AppMark"
import { CrumbsHolder } from "@/components/Crumbs"
import { Held } from "@/components/crumbs"
import { LanguagePicker } from "@/components/LanguagePicker"
import { ThemeToggle } from "@/components/ThemeToggle"
import { secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { useAppDispatch } from "@/store/hooks"
import { useSpot } from "@/store/spots"
import { spotKept } from "@/store/spotsSlice"
import { keepToken } from "./session"

const bar =
  "sticky top-0 z-20 flex min-h-14 items-center justify-between gap-3 border-b border-line bg-chrome px-5 py-2"

export function Frame() {
  const t = useText()
  const navigate = useNavigate()
  const dispatch = useAppDispatch()
  const { pathname, search } = useLocation()
  const home = useSpot("/")

  useEffect(() => {
    if (pathname === "/") {
      dispatch(spotKept({ path: "/", search }))
    }
  }, [dispatch, pathname, search])

  useEffect(() => {
    document.title = `${t("updates.title")} · ${t("app.name")}`
  }, [t])

  function leave() {
    keepToken(null)
    queryClient.clear()
    navigate("/login", { replace: true })
  }

  return (
    <CrumbsHolder>
      <div className="flex min-h-full flex-col bg-canvas text-ink">
        <header className={bar}>
          <div className="flex min-w-0 items-center gap-1.5 text-[13px]">
            <Link to={home} className="flex shrink-0 items-center gap-2 rounded-lg px-1.5 py-1 hover:bg-active">
              <AppSign className="size-5.5 shrink-0" />
              <span className="text-sm font-semibold tracking-[-0.01em] text-ink">{t("app.name")}</span>
              <span className="hidden text-faint sm:inline" aria-hidden>
                ·
              </span>
              <span className="hidden text-sm text-ink-soft sm:inline">{t("updates.title")}</span>
            </Link>
            <Tail />
          </div>

          <div className="flex shrink-0 items-center gap-2">
            <LanguagePicker />
            <ThemeToggle />
            <button type="button" onClick={leave} className={secondary}>
              {t("layout.signOut")}
            </button>
          </div>
        </header>

        <main className="flex flex-1 flex-col p-5">
          <div className="page-fill mx-auto flex w-full max-w-[1240px] flex-1 flex-col">
            <Outlet />
          </div>
        </main>
      </div>
    </CrumbsHolder>
  )
}

// The pages the release pages name after the list of the releases.
function Tail() {
  const { tail } = useContext(Held)

  return tail.map((one, at) => (
    <Fragment key={`${at}-${one.label}`}>
      <span className="text-faint" aria-hidden>
        /
      </span>
      {one.to !== undefined && at < tail.length - 1 ? (
        <Link to={one.to} className="truncate rounded-md p-1 text-muted hover:bg-active hover:text-ink">
          {one.label}
        </Link>
      ) : (
        <span className="truncate font-medium text-ink">{one.label}</span>
      )}
    </Fragment>
  ))
}
