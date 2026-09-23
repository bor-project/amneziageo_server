import { createSlice } from "@reduxjs/toolkit"
import type { PayloadAction } from "@reduxjs/toolkit"
import { storedSpots } from "./spots"
import type { Spots } from "./spots"

interface SpotsState {
  query: Spots
}

const initialState: SpotsState = {
  query: storedSpots(),
}

const spotsSlice = createSlice({
  name: "spots",
  initialState,
  reducers: {
    spotKept(state, action: PayloadAction<{ path: string; search: string }>) {
      state.query[action.payload.path] = action.payload.search
    },
  },
})

export const { spotKept } = spotsSlice.actions
export default spotsSlice.reducer
