import axios from "axios"
import { keepToken, token } from "./session"

// The path the releases answer under and the version of the service, as the service writes them into the page.
export const releases = mark("amneziageo-releases") ?? "/"

export const version = mark("amneziageo-version") ?? ""

export const http = axios.create({
  baseURL: releases,
  timeout: 15000,
})

http.interceptors.request.use((config) => {
  const held = token()
  if (held !== null && !config.headers.Authorization) {
    config.headers.Authorization = `Bearer ${held}`
  }

  return config
})

http.interceptors.response.use(
  (response) => response,
  (error) => {
    if (axios.isAxiosError(error) && error.response?.status === 401 && token() !== null) {
      keepToken(null)
    }

    throw error
  },
)

function mark(name: string): string | null {
  return document.querySelector(`meta[name="${name}"]`)?.getAttribute("content") ?? null
}
