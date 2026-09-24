import { createSlice } from '@reduxjs/toolkit'
import type { PayloadAction } from '@reduxjs/toolkit'
import type { LanguageChoice } from '@/i18n'
import type { ThemeChoice } from '@/theme/theme'
import { above, wideQuery } from '@/theme/width'
import { storedLanguage, storedTheme } from './preferences'

interface Watched {
  to: string
  started: number
}

interface UiState {
  sidebarOpen: boolean
  theme: ThemeChoice
  language: LanguageChoice
  served: LanguageChoice
  updateWatch: Watched | null
  updateHidden: string
}

const initialState: UiState = {
  sidebarOpen: above(wideQuery),
  theme: storedTheme(),
  language: storedLanguage(),
  served: 'auto',
  updateWatch: null,
  updateHidden: '',
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
    languageServed(state, action: PayloadAction<LanguageChoice>) {
      state.served = action.payload
    },
    updateWatched(state, action: PayloadAction<Watched>) {
      state.updateWatch = action.payload
      state.updateHidden = ''
    },
    updateClosed(state, action: PayloadAction<string>) {
      state.updateWatch = null
      state.updateHidden = action.payload
    },
  },
})

export const {
  sidebarToggled,
  sidebarSet,
  themeChosen,
  languageChosen,
  languageServed,
  updateWatched,
  updateClosed,
} = uiSlice.actions
export default uiSlice.reducer
