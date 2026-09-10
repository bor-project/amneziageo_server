import { createSlice } from "@reduxjs/toolkit"
import type { PayloadAction } from "@reduxjs/toolkit"
import type { DnsDraft } from "@/api/dns"
import type { PanelDraft } from "@/api/panel"
import { sessionClosed } from "./authSlice"

interface DraftState {
  panel: PanelDraft | null
  dns: DnsDraft | null
}

const initialState: DraftState = {
  panel: null,
  dns: null,
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
  },
  extraReducers: (builder) => {
    builder.addCase(sessionClosed, () => initialState)
  },
})

export const { panelDrafted, dnsDrafted } = draftSlice.actions
export default draftSlice.reducer

export function same<T>(one: T, other: T): boolean {
  return JSON.stringify(one) === JSON.stringify(other)
}
