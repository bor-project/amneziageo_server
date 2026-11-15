import type { Obfuscation } from "./configs"

export function plainObfuscation(): Obfuscation {
  return {
    jc: 0, jmin: 0, jmax: 0, s1: 0, s2: 0, s3: 0, s4: 0,
    h1: "", h2: "", h3: "", h4: "",
    i1: null, i2: null, i3: null, i4: null, i5: null,
    headerProtectionKey: "", contentPaddingAddition: "",
    rekeyAfterTime: "", rekeyTimeout: "", rejectAfterTime: "", keepaliveTimeout: "", maxHandshakeAttempts: "",
    randomTrailers: false, disableCookies: false,
  }
}

export function isPlainObfuscation(held: Obfuscation): boolean {
  const plain = plainObfuscation()

  return (Object.keys(plain) as (keyof Obfuscation)[]).every((key) =>
    plain[key] === null ? held[key] === null || held[key] === "" : held[key] === plain[key],
  )
}
