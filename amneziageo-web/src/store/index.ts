import { configureStore } from "@reduxjs/toolkit"
import auth from "./authSlice"
import drafts from "./draftSlice"
import { remember } from "./preferences"
import ui from "./uiSlice"

export const store = configureStore({
  reducer: { auth, drafts, ui },
})

let kept = store.getState().ui

store.subscribe(() => {
  const state = store.getState().ui
  if (state.theme === kept.theme && state.language === kept.language) {
    return
  }

  kept = state
  remember(state.theme, state.language)
})

export type RootState = ReturnType<typeof store.getState>
export type AppDispatch = typeof store.dispatch
