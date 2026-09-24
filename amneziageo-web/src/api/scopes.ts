export const scopes = {
  readState: "state:read",
  manageClients: "clients:write",
  manageInterfaces: "interfaces:write",
  manageRouting: "routing:write",
  manageAccess: "access:write",
  manageUpdates: "updates:write",
  readBackup: "backup:read",
  changePassword: "password:change",
} as const
