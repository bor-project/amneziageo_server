import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import { QueryClientProvider } from "@tanstack/react-query"
import { Provider } from "react-redux"
import { BrowserRouter } from "react-router-dom"
import { App } from "./App"
import { whenSessionLost } from "./api/client"
import { queryClient } from "./api/queryClient"
import { store } from "./store"
import { sessionClosed } from "./store/authSlice"
import "./index.css"

whenSessionLost(() => {
  store.dispatch(sessionClosed())
  queryClient.clear()
})

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Provider store={store}>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </QueryClientProvider>
    </Provider>
  </StrictMode>,
)
