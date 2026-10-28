import {
  Navigate,
  Outlet,
  Route,
  RouterProvider,
  ScrollRestoration,
  createBrowserRouter,
  createRoutesFromElements,
} from "react-router-dom"
import { ClientReleasePage } from "@/pages/ClientReleasePage"
import { ClientReleaseRemove } from "@/pages/ClientReleaseRemove"
import { useAppearance } from "@/theme/theme"
import { Frame } from "./Frame"
import { Home } from "./Home"
import { TokenLogin } from "./TokenLogin"
import { useToken } from "./session"

const router = createBrowserRouter(
  createRoutesFromElements(
    <Route element={<Root />}>
      <Route path="/login" element={<TokenLogin />} />
      <Route element={<Signed />}>
        <Route element={<Frame />}>
          <Route index element={<Home />} />
          <Route path="new" element={<ClientReleasePage />} />
          <Route path=":tag/edit" element={<ClientReleasePage />} />
          <Route path=":tag/delete" element={<ClientReleaseRemove />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Route>,
  ),
  { basename: new URL(".", document.baseURI).pathname },
)

export function UpdatesApp() {
  return <RouterProvider router={router} />
}

// Every page of the window: the theme and the language on the root element, the place a page was scrolled to brought
// back on the way back to it.
function Root() {
  useAppearance()

  return (
    <>
      <Outlet />
      <ScrollRestoration />
    </>
  )
}

// Opens the pages of the releases to a window signed in with the token alone.
function Signed() {
  const token = useToken()

  return token === null ? <Navigate to="/login" replace /> : <Outlet />
}
