import { useMutation } from "@tanstack/react-query"
import { client } from "./client"

export function useBackup() {
  return useMutation({
    mutationFn: async () => {
      const response = await client.get<Blob>("/backup", { responseType: "blob", timeout: 300000 })
      const url = URL.createObjectURL(response.data)
      const link = document.createElement("a")
      link.href = url
      link.download = nameOf(response.headers["content-disposition"])
      link.click()
      window.setTimeout(() => URL.revokeObjectURL(url), 10000)
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

  return plain ? plain[1] : "amneziageo-server.db"
}
