// Invented data for what the masks cannot rewrite: QR codes of configurations, links and subscriptions.
import { createRequire } from "node:module"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { deflateSync } from "node:zlib"

const web = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "amneziageo-web")
const qrcode = createRequire(join(web, "package.json"))("qrcode")

export const config = `[Interface]
PrivateKey = kP3dV8sQm2aXr7LwN0cB5yT1hF6jU9eZ4oGiRqMvWl0=
Address = 10.0.0.5/32
DNS = 10.0.0.1
MTU = 1420
Jc = 4
Jmin = 40
Jmax = 70
S1 = 52
S2 = 31
H1 = 1293847561
H2 = 2938475610
H3 = 3847561029
H4 = 4175610293

[Peer]
PublicKey = Zt7nQ2cX9vB4mL1sK8dR5wE3yH6gJ0aU2pFoTiNlVq4=
PresharedKey = 3hW9dK2qT7xL5nB8vM1cR4sY6aF0eJ2gU9oZpQiNtX8=
AllowedIPs = 0.0.0.0/0, ::/0
Endpoint = vpn.example.com:51820
PersistentKeepalive = 25
`

export const link = `vpn://${deflateSync(Buffer.from(config)).toString("base64url")}`

export const subscription = "https://vpn.example.com:8443/sub/Q2xpZW50U3Vic2NyaXB0aW9uRXhhbXBsZQ"

export function qr(text) {
  return qrcode.toDataURL(text, { errorCorrectionLevel: "M", margin: 1, scale: 8 })
}

// Puts an invented QR code of the same kind in place of every QR picture of the page; t gives the titles of the cards.
export async function swapQr(page, t) {
  const titles = { conf: t["clients.qrFile"], link: t["clients.qrLink"], subscription: t["clients.qrSubscription"] }
  const kinds = await page.eval(`[...document.querySelectorAll('img[alt="QR"]')].map((i) => {
    const titles = ${JSON.stringify(titles)}
    for (let card = i.parentElement; card; card = card.parentElement) {
      const found = Object.keys(titles).filter((k) => card.innerText.includes(titles[k]))
      if (found.length === 1) return found[0]
      if (found.length > 1) break
    }
    return "unknown"
  })`)
  if (kinds.includes("unknown")) {
    throw new Error(`a QR picture outside a card: ${kinds.join(", ")}`)
  }
  const urls = { conf: await qr(config), link: await qr(link), subscription: await qr(subscription) }
  await page.eval(`(() => {
    const urls = ${JSON.stringify(urls)}
    const kinds = ${JSON.stringify(kinds)}
    ;[...document.querySelectorAll('img[alt="QR"]')].forEach((i, n) => { i.src = urls[kinds[n]] })
  })()`)
  await page.waitFor(`[...document.querySelectorAll('img[alt="QR"]')].every((i) => i.complete)`)
  return kinds
}
