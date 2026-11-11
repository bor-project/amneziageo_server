import {
  Navigate,
  Outlet,
  Route,
  RouterProvider,
  ScrollRestoration,
  createBrowserRouter,
  createRoutesFromElements,
  useLocation,
} from "react-router-dom"
import { scopes } from "@/api/scopes"
import { Boot } from "@/components/Boot"
import { Layout } from "@/components/Layout"
import { RequireAuth, RequireScope } from "@/components/RequireAuth"
import { Cards, Sectioned } from "@/components/Section"
import { clients, interfaces, settings } from "@/components/menu"
import { RoutingLegacy, RoutingSection } from "@/components/RoutingEditor"
import { AddressGroups } from "@/pages/AddressGroups"
import { Balancers } from "@/pages/Balancers"
import { Directions } from "@/pages/Directions"
import { About } from "@/pages/About"
import { AboutText } from "@/pages/AboutText"
import { Accounts } from "@/pages/Accounts"
import { BalancerRemove } from "@/pages/BalancerRemove"
import { ClientExport, ClientSettings, ClientView, NewClient } from "@/pages/ClientPage"
import { ClientReleasePage } from "@/pages/ClientReleasePage"
import { ClientReleaseRemove } from "@/pages/ClientReleaseRemove"
import { ClientUpdates } from "@/pages/ClientUpdates"
import { ClientRemove } from "@/pages/ClientRemove"
import { ClientsRemove } from "@/pages/ClientsRemove"
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
import { OutboundRemove } from "@/pages/OutboundRemove"
import { Password } from "@/pages/Password"
import { RolePage } from "@/pages/RolePage"
import { RoleRemove } from "@/pages/RoleRemove"
import { RouteTest } from "@/pages/RouteTest"
import { RuleRemove } from "@/pages/RuleRemove"
import { Rules } from "@/pages/Rules"
import { PresetPage } from "@/pages/PresetPage"
import { PresetRemove } from "@/pages/PresetRemove"
import { Presets } from "@/pages/Presets"
import { Proxies } from "@/pages/Proxies"
import { ProxyPage, ProxyRemove } from "@/pages/ProxyPage"
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
  { from: "configs", to: "/interfaces" },
  { from: "proxies", to: "/routing/proxies" },
  { from: "rules", to: "/routing" },
  { from: "outbounds", to: "/routing/channels" },
  { from: "balancers", to: "/routing/balancers" },
  { from: "geo", to: "/routing/geo" },
  { from: "dns", to: "/routing/dns" },
  { from: "access/*", to: "/settings/users" },
  { from: "routing/ruleset", to: "/routing" },
]

const router = createBrowserRouter(
  createRoutesFromElements(
    <Route element={<Root />}>
      <Route path="/login" element={<Login />} />
      <Route path="/password" element={<Password />} />
      <Route element={<RequireAuth />}>
        <Route element={<Layout />}>
          <Route path="account/password" element={<OwnPassword />} />
          <Route path="about" element={<About />} />
          <Route path="about/:document" element={<AboutText />} />
          <Route element={<RequireScope scope={scopes.readState} />}>
            <Route index element={<Dashboard />} />
            <Route path="interfaces" element={<Sectioned title="nav.interfaces" to="/interfaces" items={interfaces} />}>
              <Route index element={<Configs />} />
              <Route path=":configId" element={<Navigate to="edit" replace />} />
              <Route element={<RequireScope scope={scopes.manageInterfaces} />}>
                <Route path="new" element={<ConfigPage />} />
                <Route path=":configId/edit" element={<ConfigPage />} />
                <Route path=":configId/delete" element={<ConfigRemove />} />
              </Route>
            </Route>
            <Route path="clients" element={<Sectioned title="nav.clients" to="/clients" items={clients} tabbed />}>
              <Route index element={<Clients />} />
              <Route path="templates" element={<Templates />} />
              <Route path="templates/default" element={<DefaultTemplate />} />
              <Route path="templates/:templateId" element={<Navigate to="edit" replace />} />
              <Route path="presets" element={<Presets />} />
              <Route path="presets/:presetId" element={<Navigate to="edit" replace />} />
              <Route element={<RequireScope scope={scopes.manageClients} />}>
                <Route path="new" element={<NewClient />} />
                <Route path="delete" element={<ClientsRemove />} />
                <Route path=":clientId/delete" element={<ClientRemove />} />
                <Route path="templates/new" element={<TemplatePage />} />
                <Route path="templates/:templateId/edit" element={<TemplatePage />} />
                <Route path="templates/:templateId/delete" element={<TemplateRemove />} />
                <Route path="presets/new" element={<PresetPage />} />
                <Route path="presets/:presetId/edit" element={<PresetPage />} />
                <Route path="presets/:presetId/delete" element={<PresetRemove />} />
              </Route>
              <Route path=":clientId" element={<ClientView />}>
                <Route index element={<Navigate to="export" replace />} />
                <Route path="export" element={<ClientExport />} />
                <Route path="edit" element={<Navigate to="../settings" replace />} />
                <Route element={<RequireScope scope={scopes.manageClients} />}>
                  <Route path="settings" element={<ClientSettings />} />
                </Route>
              </Route>
            </Route>
            <Route path="connections/*" element={<Moved />} />
            <Route path="routing" element={<RoutingSection />}>
              <Route index element={<Navigate to="rules" replace />} />
              <Route path="rules" element={<Rules />} />
              <Route element={<RequireScope scope={scopes.manageRouting} />}>
                <Route path="rules/new" element={<RoutingLegacy to="/routing/rules" />} />
                <Route path="rules/:ruleId/edit" element={<RoutingLegacy to="/routing/rules" />} />
                <Route path="rules/:ruleId/delete" element={<RuleRemove />} />
              </Route>
              <Route path="basic" element={<Navigate to="/routing/rules" replace />} />
              <Route path="test" element={<RouteTest />} />
              <Route path="channels" element={<Directions />} />
              <Route path="balancers" element={<Balancers />} />
              <Route path="address-groups" element={<AddressGroups />} />
              <Route element={<RequireScope scope={scopes.manageRouting} />}>
                <Route path="channels/new" element={<RoutingLegacy to="/routing/channels" />} />
                <Route path="channels/groups/new" element={<RoutingLegacy to="/routing/balancers" />} />
                <Route path="channels/groups/:balancerId/edit" element={<RoutingLegacy to="/routing/balancers" />} />
                <Route path="channels/groups/:balancerId/delete" element={<BalancerRemove />} />
                <Route path="channels/:outboundId/edit" element={<RoutingLegacy to="/routing/channels" />} />
                <Route path="channels/:outboundId/delete" element={<OutboundRemove />} />
              </Route>
              <Route path="geo" element={<Geo />} />
              <Route element={<RequireScope scope={scopes.manageRouting} />}>
                <Route path="geo/new" element={<GeoPage />} />
                <Route path="geo/:sourceId/edit" element={<GeoPage />} />
                <Route path="geo/:sourceId/delete" element={<GeoRemove />} />
              </Route>
              <Route path="proxies" element={<Proxies />} />
              <Route element={<RequireScope scope={scopes.manageProxies} />}>
                <Route path="proxies/new" element={<ProxyPage />} />
                <Route path="proxies/:proxyId/edit" element={<ProxyPage />} />
                <Route path="proxies/:proxyId/delete" element={<ProxyRemove />} />
              </Route>
              <Route path="dns" element={<Dns />} />
            </Route>
            {moved.map((one) => (
              <Route key={one.from} path={one.from} element={<Navigate to={one.to} replace />} />
            ))}
          </Route>
          <Route element={<RequireScope scope={scopes.manageAccess} />}>
            <Route path="settings" element={<Sectioned title="nav.settings" to="/settings" items={settings} />}>
              <Route index element={<Cards items={settings} />} />
              <Route path="server" element={<PanelServer />} />
              <Route path="certificates" element={<PanelCertificates />} />
              <Route path="subscriptions" element={<Subscriptions />} />
              <Route element={<RequireScope scope={scopes.manageClientUpdates} />}>
                <Route path="client-updates" element={<ClientUpdates />} />
                <Route path="client-updates/new" element={<ClientReleasePage />} />
                <Route path="client-updates/:tag/edit" element={<ClientReleasePage />} />
                <Route path="client-updates/:tag/delete" element={<ClientReleaseRemove />} />
              </Route>
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
    </Route>,
  ),
  { basename: new URL(".", document.baseURI).pathname },
)

export function App() {
  return <RouterProvider router={router} />
}

// Every page of the panel: the theme and the language on the root element, the session restored before anything is
// shown, and the place a page was scrolled to brought back on the way back to it.
function Root() {
  useAppearance()

  return (
    <>
      <Boot>
        <Outlet />
      </Boot>
      <ScrollRestoration />
    </>
  )
}

// Leads an address of the part the interfaces, the clients and the templates shared before to where they are now.
function Moved() {
  const { pathname, search } = useLocation()

  return <Navigate to={movedTo(pathname) + search} replace />
}

function movedTo(path: string): string {
  const rest = path.replace(/^\/connections/, "")

  if (rest === "/templates/clients" || rest === "/templates/interfaces") {
    return "/clients/templates"
  }

  if (rest.startsWith("/templates")) {
    return `/clients${rest}`
  }

  if (rest.startsWith("/clients")) {
    return rest.replace(/^(\/clients\/\d+)\/edit$/, "$1/settings")
  }

  return rest.startsWith("/interfaces") ? rest : "/clients"
}
