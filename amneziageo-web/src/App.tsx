import { Navigate, Route, Routes } from "react-router-dom"
import { scopes } from "@/api/scopes"
import { Boot } from "@/components/Boot"
import { Layout } from "@/components/Layout"
import { RequireAuth, RequireScope } from "@/components/RequireAuth"
import { Cards, Sectioned } from "@/components/Section"
import { connections, routing, settings } from "@/components/menu"
import { Accounts } from "@/pages/Accounts"
import { BalancerPage } from "@/pages/BalancerPage"
import { BasicRouting } from "@/pages/BasicRouting"
import { BalancerRemove } from "@/pages/BalancerRemove"
import { Channels } from "@/pages/Channels"
import { ClientExport } from "@/pages/ClientExport"
import { ClientPage } from "@/pages/ClientPage"
import { ClientRemove } from "@/pages/ClientRemove"
import { Clients } from "@/pages/Clients"
import { ConfigPage } from "@/pages/ConfigPage"
import { ConfigRemove } from "@/pages/ConfigRemove"
import { Configs } from "@/pages/Configs"
import { Dashboard } from "@/pages/Dashboard"
import { DefaultTemplate } from "@/pages/DefaultTemplate"
import { Diagnostics } from "@/pages/Diagnostics"
import { Dns } from "@/pages/Dns"
import { Geo } from "@/pages/Geo"
import { GeoPage } from "@/pages/GeoPage"
import { GeoRemove } from "@/pages/GeoRemove"
import { Login } from "@/pages/Login"
import { OwnPassword } from "@/pages/OwnPassword"
import { OutboundPage } from "@/pages/OutboundPage"
import { OutboundRemove } from "@/pages/OutboundRemove"
import { Password } from "@/pages/Password"
import { RolePage } from "@/pages/RolePage"
import { RoleRemove } from "@/pages/RoleRemove"
import { RouteTest } from "@/pages/RouteTest"
import { RulePage } from "@/pages/RulePage"
import { RuleRemove } from "@/pages/RuleRemove"
import { Rules } from "@/pages/Rules"
import { Ruleset } from "@/pages/Ruleset"
import { PanelCertificates, PanelServer } from "@/pages/Settings"
import { Subscriptions } from "@/pages/Subscriptions"
import { TemplatePage } from "@/pages/TemplatePage"
import { TemplateRemove } from "@/pages/TemplateRemove"
import { Templates } from "@/pages/Templates"
import { TokenPage } from "@/pages/TokenPage"
import { TokenRemove } from "@/pages/TokenRemove"
import { UserPage } from "@/pages/UserPage"
import { UserPassword } from "@/pages/UserPassword"
import { UserRemove } from "@/pages/UserRemove"
import { useAppearance } from "@/theme/theme"

const moved: { from: string; to: string }[] = [
  { from: "configs", to: "/connections" },
  { from: "clients", to: "/connections/clients" },
  { from: "proxies", to: "/connections" },
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
            <Route path="account/password" element={<OwnPassword />} />
            <Route element={<RequireScope scope={scopes.readState} />}>
              <Route index element={<Dashboard />} />
              <Route path="connections" element={<Sectioned title="nav.connections" items={connections} />}>
                <Route index element={<Cards items={connections} />} />
                <Route path="interfaces" element={<Configs />} />
                <Route path="interfaces/:configId" element={<Navigate to="edit" replace />} />
                <Route element={<RequireScope scope={scopes.manageInterfaces} />}>
                  <Route path="interfaces/new" element={<ConfigPage />} />
                  <Route path="interfaces/:configId/edit" element={<ConfigPage />} />
                  <Route path="interfaces/:configId/delete" element={<ConfigRemove />} />
                </Route>
                <Route path="clients" element={<Clients />} />
                <Route path="clients/:clientId" element={<Navigate to="export" replace />} />
                <Route path="clients/:clientId/export" element={<ClientExport />} />
                <Route element={<RequireScope scope={scopes.manageClients} />}>
                  <Route path="clients/new" element={<ClientPage />} />
                  <Route path="clients/:clientId/edit" element={<ClientPage />} />
                  <Route path="clients/:clientId/delete" element={<ClientRemove />} />
                </Route>
                <Route path="templates" element={<Templates />} />
                <Route path="templates/default" element={<DefaultTemplate />} />
                <Route path="templates/clients" element={<Navigate to="/connections/templates" replace />} />
                <Route path="templates/interfaces" element={<Navigate to="/connections/templates" replace />} />
                <Route path="templates/:templateId" element={<Navigate to="edit" replace />} />
                <Route element={<RequireScope scope={scopes.manageClients} />}>
                  <Route path="templates/new" element={<TemplatePage />} />
                  <Route path="templates/:templateId/edit" element={<TemplatePage />} />
                  <Route path="templates/:templateId/delete" element={<TemplateRemove />} />
                </Route>
              </Route>
              <Route path="routing" element={<Sectioned title="nav.routing" items={routing} />}>
                <Route index element={<Cards items={routing} />} />
                <Route path="rules" element={<Rules />} />
                <Route element={<RequireScope scope={scopes.manageRouting} />}>
                  <Route path="rules/new" element={<RulePage />} />
                  <Route path="rules/:ruleId/edit" element={<RulePage />} />
                  <Route path="rules/:ruleId/delete" element={<RuleRemove />} />
                </Route>
                <Route path="basic" element={<BasicRouting />} />
                <Route path="test" element={<RouteTest />} />
                <Route path="channels" element={<Channels />} />
                <Route element={<RequireScope scope={scopes.manageRouting} />}>
                  <Route path="channels/new" element={<OutboundPage />} />
                  <Route path="channels/groups/new" element={<BalancerPage />} />
                  <Route path="channels/groups/:balancerId/edit" element={<BalancerPage />} />
                  <Route path="channels/groups/:balancerId/delete" element={<BalancerRemove />} />
                  <Route path="channels/:outboundId/edit" element={<OutboundPage />} />
                  <Route path="channels/:outboundId/delete" element={<OutboundRemove />} />
                </Route>
                <Route path="geo" element={<Geo />} />
                <Route element={<RequireScope scope={scopes.manageRouting} />}>
                  <Route path="geo/new" element={<GeoPage />} />
                  <Route path="geo/:sourceId/edit" element={<GeoPage />} />
                  <Route path="geo/:sourceId/delete" element={<GeoRemove />} />
                </Route>
                <Route path="dns" element={<Dns />} />
                <Route element={<RequireScope scope={scopes.manageRouting} />}>
                  <Route path="ruleset" element={<Ruleset />} />
                </Route>
              </Route>
              {moved.map((one) => (
                <Route key={one.from} path={one.from} element={<Navigate to={one.to} replace />} />
              ))}
            </Route>
            <Route element={<RequireScope scope={scopes.manageAccess} />}>
              <Route path="settings" element={<Sectioned title="nav.settings" items={settings} />}>
                <Route index element={<Cards items={settings} />} />
                <Route path="server" element={<PanelServer />} />
                <Route path="certificates" element={<PanelCertificates />} />
                <Route path="subscriptions" element={<Subscriptions />} />
                <Route path="users" element={<Accounts />} />
                <Route path="users/new" element={<UserPage />} />
                <Route path="users/roles/new" element={<RolePage />} />
                <Route path="users/roles/:name/edit" element={<RolePage />} />
                <Route path="users/roles/:name/delete" element={<RoleRemove />} />
                <Route path="users/tokens/new" element={<TokenPage />} />
                <Route path="users/tokens/:tokenId/delete" element={<TokenRemove />} />
                <Route path="users/:name/edit" element={<UserPage />} />
                <Route path="users/:name/password" element={<UserPassword />} />
                <Route path="users/:name/delete" element={<UserRemove />} />
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
