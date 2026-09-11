import { ApiTokens } from "@/components/ApiTokens"
import { Roles } from "@/components/Roles"
import { Users } from "@/components/Users"

export function Accounts() {
  return (
    <div>
      <Users />
      <Roles />
      <ApiTokens />
    </div>
  )
}
