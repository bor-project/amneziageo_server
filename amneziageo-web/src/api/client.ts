import axios from "axios"
import type { InternalAxiosRequestConfig } from "axios"
import { access, drop, keep, refresh } from "./tokens"

type Retried = InternalAxiosRequestConfig & { retried?: boolean }

export const client = axios.create({
  baseURL: "/api",
  timeout: 15000,
})

const bare = axios.create({
  baseURL: "/api",
  timeout: 15000,
})

let renewal: Promise<boolean> | null = null

let lost: (() => void) | null = null

export function whenSessionLost(handler: () => void): void {
  lost = handler
}

export function renew(): Promise<boolean> {
  const token = refresh()
  if (!token) {
    return Promise.resolve(false)
  }

  renewal ??= bare
    .post("/auth/refresh", { refresh: token })
    .then((response) => {
      keep(response.data)
      return true
    })
    .catch(() => {
      drop()
      return false
    })
    .finally(() => {
      renewal = null
    })

  return renewal
}

client.interceptors.request.use((config) => {
  const token = access()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }

  return config
})

client.interceptors.response.use(
  (response) => response,
  async (error) => {
    const config = error.config as Retried | undefined
    if (!config || config.retried || error.response?.status !== 401) {
      throw error
    }

    config.retried = true
    if (!(await renew())) {
      drop()
      lost?.()
      throw error
    }

    return client(config)
  },
)

export { bare }
