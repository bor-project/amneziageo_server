import { Navigate, Route, Routes } from "react-router-dom"
import { scopes } from "@/api/scopes"
import { Boot } from "@/components/Boot"
import { Layout } from "@/components/Layout"
import { RequireAuth, RequireScope } from "@/components/RequireAuth"
import { Roles } from "@/components/Roles"
import { Users } from "@/components/Users"
import { Access } from "@/pages/Access"
import { Clients } from "@/pages/Clients"
import { Configs } from "@/pages/Configs"
import { Dashboard } from "@/pages/Dashboard"
import { Login } from "@/pages/Login"
import { Password } from "@/pages/Password"
import { Balancers } from "@/pages/Balancers"
import { Dns } from "@/pages/Dns"
import { Proxies } from "@/pages/Proxies"
import { Geo } from "@/pages/Geo"
import { Outbounds } from "@/pages/Outbounds"
import { Rules } from "@/pages/Rules"
import { Settings } from "@/pages/Settings"
import { useAppearance } from "@/theme/theme"

export function App() {
  useAppearance()

  return (
    <Boot>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/password" element={<Password />} />
        <Route element={<RequireAuth />}>
          <Route element={<Layout />}>
            <Route element={<RequireScope scope={scopes.readState} />}>
              <Route index element={<Dashboard />} />
              <Route path="configs" element={<Configs />} />
              <Route path="clients" element={<Clients />} />
              <Route path="geo" element={<Geo />} />
              <Route path="outbounds" element={<Outbounds />} />
              <Route path="balancers" element={<Balancers />} />
              <Route path="rules" element={<Rules />} />
              <Route path="dns" element={<Dns />} />
              <Route path="proxies" element={<Proxies />} />
            </Route>
            <Route element={<RequireScope scope={scopes.manageAccess} />}>
              <Route path="settings" element={<Settings />} />
              <Route path="access" element={<Access />}>
                <Route index element={<Users />} />
                <Route path="roles" element={<Roles />} />
              </Route>
            </Route>
          </Route>
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Boot>
  )
}
