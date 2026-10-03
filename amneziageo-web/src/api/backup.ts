import { useMutation } from "@tanstack/react-query"
import { bare, client } from "./client"

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

// Hands a backup to the panel, which checks it, puts it in place of its database and starts over on it; settles once
// the panel answers again.
export function useRestore() {
  return useMutation({
    mutationFn: async ({ file, progress }: { file: File; progress: (share: number) => void }) => {
      await client.post("/backup/restore", file, {
        headers: { "Content-Type": "application/octet-stream" },
        timeout: 0,
        onUploadProgress: (event) => progress(event.total ? event.loaded / event.total : 0),
      })
      await restarted()
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

// Waits for the panel to go down and to answer again, or for a while when it starts over too fast to be seen down.
async function restarted() {
  const started = Date.now()
  let down = false
  while (Date.now() - started < 120000) {
    await pause(1000)
    const up = await bare
      .get("/health", { timeout: 3000 })
      .then(() => true)
      .catch(() => false)
    if (!up) {
      down = true
    } else if (down || Date.now() - started > 20000) {
      return
    }
  }
}

function pause(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}
