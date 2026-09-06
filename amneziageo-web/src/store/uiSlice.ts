import { createSlice } from '@reduxjs/toolkit'
import type { PayloadAction } from '@reduxjs/toolkit'
import type { LanguageChoice } from '@/i18n'
import type { ThemeChoice } from '@/theme/theme'
import { storedLanguage, storedTheme } from './preferences'

interface UiState {
  sidebarOpen: boolean
  theme: ThemeChoice
  language: LanguageChoice
}

const initialState: UiState = {
  sidebarOpen: true,
  theme: storedTheme(),
  language: storedLanguage(),
}

const uiSlice = createSlice({
  name: 'ui',
  initialState,
  reducers: {
    sidebarToggled(state) {
      state.sidebarOpen = !state.sidebarOpen
    },
    sidebarSet(state, action: PayloadAction<boolean>) {
      state.sidebarOpen = action.payload
    },
    themeChosen(state, action: PayloadAction<ThemeChoice>) {
      state.theme = action.payload
    },
    languageChosen(state, action: PayloadAction<LanguageChoice>) {
      state.language = action.payload
    },
  },
})

export const { sidebarToggled, sidebarSet, themeChosen, languageChosen } = uiSlice.actions
export default uiSlice.reducer
