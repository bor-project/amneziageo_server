import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import { QueryClientProvider } from "@tanstack/react-query"
import { Provider } from "react-redux"
import { takeReleaseRoad } from "@/api/clientUpdates"
import { queryClient } from "@/api/queryClient"
import { store } from "@/store"
import { UpdatesApp } from "./UpdatesApp"
import { http } from "./http"
import "@/index.css"

takeReleaseRoad({ http, base: "", home: "/" })

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Provider store={store}>
      <QueryClientProvider client={queryClient}>
        <UpdatesApp />
      </QueryClientProvider>
    </Provider>
  </StrictMode>,
)
