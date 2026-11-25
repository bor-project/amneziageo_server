import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"
import { defaultProxyName } from "@/components/names"
import type { Holding } from "./firewall"

export interface PanelDraft {
  listen: string[]
  domains: string[]
  port: number
  path: string
  certificate: string
  certificateKey: string
  language: string
  prereleases: boolean
  nameTemplate: string
  proxyNameTemplate: string
}

export interface PanelPlace {
  port: number
  path: string
  secure: boolean
}

export interface Panel extends PanelDraft {
  opened: boolean
  certificates: string[]
  addresses: string[]
  certificateRoot: string
  pending: boolean
  secure: boolean
  running: PanelPlace
}

export interface NameSample {
  values: Record<string, string>
  stamp: string
}

export interface CertificateDetails {
  name: string
  subject: string
  issuer: string
  subjectAlternativeNames: string[]
  notBefore: string
  notAfter: string
  keyAlgorithm: string
  keyOid: string
  keyBits: number | null
  signatureAlgorithm: string
  sha256: string
}

export interface PanelCertificate {
  enabled: boolean
  certificates: CertificateDetails[]
}

export function usePanelCertificate() {
  return useQuery({
    queryKey: ["panel", "certificate"],
    queryFn: async () => (await client.get<PanelCertificate>("/panel/certificate")).data,
  })
}

export function useDownloadPanelCertificate() {
  return useMutation({
    mutationFn: async (chain: boolean) => {
      const response = await client.get<Blob>(`/panel/certificate/${chain ? "chain" : "leaf"}.pem`, { responseType: "blob" })
      const url = URL.createObjectURL(response.data)
      const link = document.createElement("a")
      link.href = url
      link.download = chain ? "panel-chain.pem" : "panel-certificate.pem"
      link.click()
      window.setTimeout(() => URL.revokeObjectURL(url), 10000)
    },
  })
}

export function usePanel(enabled = true) {
  return useQuery({
    queryKey: ["panel"],
    queryFn: async () => (await client.get<Panel>("/panel")).data,
    enabled,
  })
}

export function useNameSample(enabled = true) {
  return useQuery({
    queryKey: ["panel", "names"],
    queryFn: async () => (await client.get<NameSample>("/panel/names")).data,
    enabled,
  })
}

export function useProxyNameSample(enabled = true) {
  return useQuery({
    queryKey: ["panel", "proxy-names"],
    queryFn: async () => (await client.get<NameSample>("/panel/proxy-names")).data,
    enabled,
  })
}

export function useSavePanel() {
  return useRefreshing((draft: PanelDraft & Holding) => client.put("/panel", draft))
}

export function useRestartPanel() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async () => {
      const saved = queryClient.getQueryData<Panel>(["panel"])
      const next = saved === undefined ? null : movedTo(saved)
      await client.post("/panel/restart")
      if (saved === undefined || next === null) {
        await started()
        return
      }

      await reached(next, saved.path)
      window.location.replace(next)
    },
    onSettled: async () => {
      await queryClient.invalidateQueries()
    },
  })
}

export function draftOf(panel: Panel): PanelDraft {
  return {
    listen: panel.listen,
    domains: panel.domains,
    port: panel.port,
    path: panel.path,
    certificate: panel.certificate,
    certificateKey: panel.certificateKey,
    language: panel.language,
    prereleases: panel.prereleases,
    nameTemplate: panel.nameTemplate,
    proxyNameTemplate: panel.proxyNameTemplate ?? defaultProxyName,
  }
}

async function started() {
  const until = Date.now() + 60000
  while (Date.now() < until) {
    await pause(1000)
    const answer = await client.get<Panel>("/panel", { timeout: 3000 }).catch(() => null)
    if (answer?.data?.pending === false) {
      return
    }
  }
}

function movedTo(saved: Panel): string | null {
  const here = new URL(window.location.href)
  const next = new URL(here.href)
  const base = new URL(".", document.baseURI).pathname
  const direct = portOf(here) === saved.running.port && (here.protocol === "https:") === saved.running.secure
  if (direct) {
    next.protocol = saved.secure ? "https:" : "http:"
    next.port = String(saved.port)
  }

  next.pathname = saved.path + (here.pathname.startsWith(base) ? here.pathname.slice(base.length) : "")

  return next.href === here.href ? null : next.href
}

function portOf(address: URL): number {
  if (address.port.length > 0) {
    return Number(address.port)
  }

  return address.protocol === "https:" ? 443 : 80
}

async function reached(next: string, path: string) {
  const target = new URL(next)
  const same = target.origin === window.location.origin
  const until = Date.now() + 60000
  while (Date.now() < until) {
    await pause(1000)
    const answered = same
      ? await client
          .get<Panel>(`${path}api/panel`, { baseURL: "", timeout: 3000 })
          .then((answer) => answer.data?.pending === false)
          .catch(() => false)
      : await fetch(new URL(`${path}api/health`, target.origin), { mode: "no-cors", cache: "no-store" })
          .then(() => true)
          .catch(() => false)
    if (answered) {
      return
    }
  }
}

function pause(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function useRefreshing<TArgs>(call: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: call,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["panel"] })
      await queryClient.invalidateQueries({ queryKey: ["firewall", "port"] })
    },
  })
}
