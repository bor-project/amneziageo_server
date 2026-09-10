import { createSlice } from "@reduxjs/toolkit"
import type { PayloadAction } from "@reduxjs/toolkit"
import type { DnsDraft } from "@/api/dns"
import type { PanelDraft } from "@/api/panel"
import type { SubscriptionDraft } from "@/api/subscription"
import { sessionClosed } from "./authSlice"

interface DraftState {
  panel: PanelDraft | null
  dns: DnsDraft | null
  subscription: SubscriptionDraft | null
}

const initialState: DraftState = {
  panel: null,
  dns: null,
  subscription: null,
}

const draftSlice = createSlice({
  name: "drafts",
  initialState,
  reducers: {
    panelDrafted(state, action: PayloadAction<PanelDraft | null>) {
      state.panel = action.payload
    },
    dnsDrafted(state, action: PayloadAction<DnsDraft | null>) {
      state.dns = action.payload
    },
    subscriptionDrafted(state, action: PayloadAction<SubscriptionDraft | null>) {
      state.subscription = action.payload
    },
  },
  extraReducers: (builder) => {
    builder.addCase(sessionClosed, () => initialState)
  },
})

export const { panelDrafted, dnsDrafted, subscriptionDrafted } = draftSlice.actions
export default draftSlice.reducer

export function same<T>(one: T, other: T): boolean {
  return JSON.stringify(one) === JSON.stringify(other)
}
