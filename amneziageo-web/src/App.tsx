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
import { Interfaces } from "@/pages/Interfaces"
import { Login } from "@/pages/Login"
import { Password } from "@/pages/Password"
import { General } from "@/pages/General"
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
              <Route path="interfaces" element={<Interfaces />} />
              <Route path="clients" element={<Clients />} />
            </Route>
            <Route path="settings/general" element={<General />} />
            <Route element={<RequireScope scope={scopes.manageAccess} />}>
              <Route path="settings/access" element={<Access />}>
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
