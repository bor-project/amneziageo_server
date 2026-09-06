import { createSlice } from "@reduxjs/toolkit"
import type { PayloadAction } from "@reduxjs/toolkit"
import type { Account, Session } from "@/api/auth"

export type AuthStatus = "unknown" | "anonymous" | "active" | "must-change"

interface AuthState {
  status: AuthStatus
  user: Account | null
}

const initialState: AuthState = {
  status: "unknown",
  user: null,
}

const authSlice = createSlice({
  name: "auth",
  initialState,
  reducers: {
    sessionOpened(state, action: PayloadAction<Session>) {
      state.status = action.payload.mustChangePassword ? "must-change" : "active"
      state.user = action.payload.user
    },
    sessionRestored(state, action: PayloadAction<Account>) {
      state.status = "active"
      state.user = action.payload
    },
    sessionClosed(state) {
      state.status = "anonymous"
      state.user = null
    },
  },
})

export const { sessionOpened, sessionRestored, sessionClosed } = authSlice.actions
export default authSlice.reducer

export function holds(user: Account | null, scope: string): boolean {
  return user !== null && user.scopes.includes(scope)
}
