import { ApiTokens } from "@/components/ApiTokens"
import { Roles } from "@/components/Roles"
import { Users } from "@/components/Users"

export function Accounts() {
  return (
    <div className="mt-4 flex flex-col gap-4">
      <Users />
      <Roles />
      <ApiTokens />
    </div>
  )
}
