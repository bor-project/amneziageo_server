import { configureStore } from "@reduxjs/toolkit"
import auth from "./authSlice"
import drafts from "./draftSlice"
import { remember } from "./preferences"
import { rememberSpots } from "./spots"
import spots from "./spotsSlice"
import ui from "./uiSlice"

export const store = configureStore({
  reducer: { auth, drafts, spots, ui },
})

let kept = store.getState().ui
let keptSpots = store.getState().spots.query

store.subscribe(() => {
  const state = store.getState().ui

  if (state.theme !== kept.theme || state.language !== kept.language) {
    kept = state
    remember(state.theme, state.language)
  }

  const now = store.getState().spots.query

  if (now !== keptSpots) {
    keptSpots = now
    rememberSpots(now)
  }
})

export type RootState = ReturnType<typeof store.getState>
export type AppDispatch = typeof store.dispatch
