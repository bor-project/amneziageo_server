import { Navigate, Route, Routes } from "react-router-dom"
import { scopes } from "@/api/scopes"
import { Boot } from "@/components/Boot"
import { Layout } from "@/components/Layout"
import { RequireAuth, RequireScope } from "@/components/RequireAuth"
import { Tabbed } from "@/components/Tabs"
import type { Tab } from "@/components/Tabs"
import { Accounts } from "@/pages/Accounts"
import { Channels } from "@/pages/Channels"
import { Clients } from "@/pages/Clients"
import { Configs } from "@/pages/Configs"
import { Dashboard } from "@/pages/Dashboard"
import { Diagnostics } from "@/pages/Diagnostics"
import { Dns } from "@/pages/Dns"
import { Geo } from "@/pages/Geo"
import { Login } from "@/pages/Login"
import { Password } from "@/pages/Password"
import { Proxies } from "@/pages/Proxies"
import { Rules } from "@/pages/Rules"
import { PanelCertificates, PanelServer } from "@/pages/Settings"
import { Templates } from "@/pages/Templates"
import { useAppearance } from "@/theme/theme"

const connections: Tab[] = [
  { to: "/connections", label: "tab.interfaces", scope: scopes.readState, end: true },
  { to: "/connections/clients", label: "tab.clients", scope: scopes.readState },
  { to: "/connections/templates", label: "tab.templates", scope: scopes.readState },
  { to: "/connections/proxies", label: "tab.proxies", scope: scopes.readState },
]

const routing: Tab[] = [
  { to: "/routing", label: "tab.rules", scope: scopes.readState, end: true },
  { to: "/routing/channels", label: "tab.channels", scope: scopes.readState },
  { to: "/routing/geo", label: "tab.geo", scope: scopes.readState },
  { to: "/routing/dns", label: "tab.dns", scope: scopes.readState },
]

const settings: Tab[] = [
  { to: "/settings", label: "tab.server", scope: scopes.manageAccess, end: true },
  { to: "/settings/certificates", label: "tab.certificates", scope: scopes.manageAccess },
  { to: "/settings/users", label: "tab.users", scope: scopes.manageAccess },
  { to: "/settings/diagnostics", label: "tab.diagnostics", scope: scopes.manageAccess },
]

const moved: { from: string; to: string }[] = [
  { from: "configs", to: "/connections" },
  { from: "clients", to: "/connections/clients" },
  { from: "proxies", to: "/connections/proxies" },
  { from: "rules", to: "/routing" },
  { from: "outbounds", to: "/routing/channels" },
  { from: "balancers", to: "/routing/channels" },
  { from: "geo", to: "/routing/geo" },
  { from: "dns", to: "/routing/dns" },
  { from: "access/*", to: "/settings/users" },
]

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
              <Route path="connections" element={<Tabbed title="nav.connections" tabs={connections} />}>
                <Route index element={<Configs />} />
                <Route path="clients" element={<Clients />} />
                <Route path="templates" element={<Templates />} />
                <Route path="proxies" element={<Proxies />} />
              </Route>
              <Route path="routing" element={<Tabbed title="nav.routing" tabs={routing} />}>
                <Route index element={<Rules />} />
                <Route path="channels" element={<Channels />} />
                <Route path="geo" element={<Geo />} />
                <Route path="dns" element={<Dns />} />
              </Route>
              {moved.map((one) => (
                <Route key={one.from} path={one.from} element={<Navigate to={one.to} replace />} />
              ))}
            </Route>
            <Route element={<RequireScope scope={scopes.manageAccess} />}>
              <Route path="settings" element={<Tabbed title="nav.settings" tabs={settings} />}>
                <Route index element={<PanelServer />} />
                <Route path="certificates" element={<PanelCertificates />} />
                <Route path="users" element={<Accounts />} />
                <Route path="diagnostics" element={<Diagnostics />} />
              </Route>
            </Route>
          </Route>
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Boot>
  )
}
