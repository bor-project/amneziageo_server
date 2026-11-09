import { useMutation, useQueryClient } from "@tanstack/react-query"
import { client } from "./client"

export interface ExchangePlan {
  newGroups?: string[]
  replacedGroups?: string[]
  newRules: string[]
  replacedRules: string[]
  replacesBasic: boolean
  newPresets: string[]
  replacedPresets: string[]
  newSources: string[]
  replacedSources: string[]
  missingOutbounds: string[]
  missingClients: string[]
  detached: string[]
  doc: {
    rules: { name: string }[]
    basic: { direct: string[]; block: string[] }
    presets: { name: string }[]
    sources: { name: string }[]
    addressGroups?: { name: string }[]
  }
}

export interface ExchangeAnswer {
  added: string[]
  replaced: string[]
  basicReplaced: boolean
  refused: string[]
  detached: string[]
}

// Takes the routing of the panel as a file the panel names.
export function useRoutingExport() {
  return useMutation({
    mutationFn: async () => {
      const response = await client.get<Blob>("/routing/export", { responseType: "blob", timeout: 300000 })
      const url = URL.createObjectURL(response.data)
      const link = document.createElement("a")
      link.href = url
      link.download = nameOf(response.headers["content-disposition"])
      link.click()
      window.setTimeout(() => URL.revokeObjectURL(url), 10000)
    },
  })
}

// Reads a routing file in and answers the plan of what it would add and replace, without writing anything.
export function useRoutingImport() {
  return useMutation({
    mutationFn: async ({ file }: { file: File }) => {
      const answer = await client.post<ExchangePlan>("/routing/import", file, {
        headers: { "Content-Type": "application/json" },
        timeout: 300000,
      })

      return answer.data
    },
  })
}

// Lays a routing file over the routing of the panel and answers what it took in; the lists of the panel read
// themselves anew once the answer comes.
export function useRoutingApply() {
  const keep = useQueryClient()

  return useMutation({
    mutationFn: async ({ file }: { file: File }) => {
      const answer = await client.post<ExchangeAnswer>("/routing/import/apply", file, {
        headers: { "Content-Type": "application/json" },
        timeout: 300000,
      })

      return answer.data
    },
    onSuccess: async () => {
      await keep.invalidateQueries({ queryKey: ["rules"] })
      await keep.invalidateQueries({ queryKey: ["address-groups"] })
      await keep.invalidateQueries({ queryKey: ["presets"] })
      await keep.invalidateQueries({ queryKey: ["geo"] })
    },
  })
}

function nameOf(disposition: unknown): string {
  const text = typeof disposition === "string" ? disposition : ""
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(text)
  if (encoded) {
    return decodeURIComponent(encoded[1])
  }

  const plain = /filename="?([^";]+)"?/i.exec(text)

  return plain ? plain[1] : "amneziageo-routing.json"
}
