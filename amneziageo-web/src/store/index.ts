import { configureStore } from '@reduxjs/toolkit'
import ui from './uiSlice'

export const store = configureStore({
  reducer: { ui },
})

export type RootState = ReturnType<typeof store.getState>
export type AppDispatch = typeof store.dispatch
