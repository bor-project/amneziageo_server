import type { Obfuscation } from "@/api/configs"

const headerNonce = 12
const maxJunk = 1280
const handshakeGap = 56

export function headerProtectionChange(cover: Obfuscation, replaceKey = true) {
  const s1 = headerJunk(cover.s1)
  const s2 = headerJunk(cover.s2)
  const alike = s1 + handshakeGap === s2
  const changedFirst = s1 !== cover.s1

  return {
    headerProtectionKey: !replaceKey && cover.headerProtectionKey.length > 0 ? cover.headerProtectionKey : randomHeaderKey(),
    s1: alike && changedFirst ? s1 + 1 : s1,
    s2: alike && !changedFirst ? s2 - 1 : s2,
    s3: headerJunk(cover.s3),
    s4: headerJunk(cover.s4),
    contentPaddingAddition: filled(cover.contentPaddingAddition, "0"),
    rekeyAfterTime: filled(cover.rekeyAfterTime, "120"),
    rekeyTimeout: filled(cover.rekeyTimeout, "5"),
    rejectAfterTime: filled(cover.rejectAfterTime, "180"),
    keepaliveTimeout: filled(cover.keepaliveTimeout, "10"),
    maxHandshakeAttempts: filled(cover.maxHandshakeAttempts, "18"),
  }
}

export function headerProtectionMtu(mtu: number, previousKey: string) {
  return previousKey.length === 0 && mtu === 1420 ? 1400 : mtu
}

function randomHeaderKey() {
  const bytes = new Uint8Array(32)
  crypto.getRandomValues(bytes)
  return btoa(Array.from(bytes, (byte) => String.fromCharCode(byte)).join(""))
}

function headerJunk(size: number) {
  return Number.isFinite(size) ? Math.min(maxJunk, Math.max(headerNonce, Math.trunc(size))) : headerNonce
}

function filled(value: string, fallback: string) {
  return value.trim().length === 0 ? fallback : value
}
