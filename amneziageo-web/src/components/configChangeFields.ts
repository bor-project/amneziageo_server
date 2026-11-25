import type { Text, TextKey } from "@/i18n"

const captions: Record<string, TextKey> = {
  address: "configs.address", dns: "configs.dns", mtu: "configs.mtu",
  privatekey: "configs.private", publickey: "configs.public", presharedkey: "configs.preshared",
  endpoint: "configs.endpoint", allowedips: "configs.allowed", persistentkeepalive: "configs.keepalive",
  jc: "configs.jc", jmin: "configs.jmin", jmax: "configs.jmax",
  s1: "configs.s1", s2: "configs.s2", s3: "configs.s3", s4: "configs.s4",
  h1: "configs.h1", h2: "configs.h2", h3: "configs.h3", h4: "configs.h4",
  i1: "configs.i1", i2: "configs.i2", i3: "configs.i3", i4: "configs.i4", i5: "configs.i5",
  headerprotectionkey: "configs.headerKey", contentpaddingaddition: "configs.padding",
  rekeyaftertime: "configs.rekeyAfter", rekeytimeout: "configs.rekeyTimeout", rejectaftertime: "configs.rejectAfter",
  keepalivetimeout: "configs.keepaliveTimeout", maxhandshakeattempts: "configs.attempts",
  randomtrailers: "configs.trailers", disablecookies: "configs.cookies",
  "# amneziageo services": "configs.servicesPort",
}

export function configChangeField(t: Text, field: string): string {
  const name = field.replace(/^\[[^\]]+\]\d*\./, "")
  const caption = captions[name.toLowerCase()]
  return caption === undefined ? name : t(caption)
}
