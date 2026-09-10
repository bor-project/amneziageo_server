export interface Span {
  six: boolean
  bits: number
  network: bigint
  own: bigint
  size: bigint
}

export interface Point {
  six: boolean
  value: bigint
}

export function span(text: string): Span | null {
  const parts = text.trim().split("/")
  const point = parse(parts[0] ?? "")
  if (point === null) {
    return null
  }

  const width = point.six ? 128 : 32
  const bits = bitsOf(parts[1], width)
  if (bits < 0 || bits > width) {
    return null
  }

  const size = 1n << BigInt(width - bits)

  return { six: point.six, bits, network: point.value - (point.value % size), own: point.value, size }
}

export function parse(text: string): Point | null {
  return text.includes(":") ? readSix(text) : readFour(text)
}

export function write(six: boolean, value: bigint): string {
  return six ? writeSix(value) : writeFour(value)
}

export function nth(range: Span, number: bigint): string {
  return write(range.six, range.network + number)
}

export function numberOf(range: Span, address: string): bigint | null {
  const point = parse(address.split("/")[0] ?? "")
  if (point === null || point.six !== range.six) {
    return null
  }

  const number = point.value - range.network

  return number >= 0n && number < range.size ? number : null
}

export function reserved(range: Span, number: bigint): boolean {
  if (number === range.own - range.network) {
    return true
  }

  if (range.size <= 2n) {
    return false
  }

  return number === 0n || (!range.six && number === range.size - 1n)
}

function bitsOf(tail: string | undefined, width: number): number {
  if (tail === undefined) {
    return width
  }

  return /^\d+$/.test(tail) ? Number(tail) : -1
}

function readFour(text: string): Point | null {
  const parts = text.split(".")
  if (parts.length !== 4 || parts.some((one) => !/^\d{1,3}$/.test(one) || Number(one) > 255)) {
    return null
  }

  return { six: false, value: parts.reduce((sum, one) => sum * 256n + BigInt(one), 0n) }
}

function readSix(text: string): Point | null {
  const halves = text.split("::")
  if (halves.length > 2) {
    return null
  }

  const head = groups(halves[0] ?? "")
  const tail = halves.length === 2 ? groups(halves[1] ?? "") : []
  const gap = 8 - head.length - tail.length
  if (halves.length === 1 ? head.length !== 8 : gap < 1) {
    return null
  }

  const all = [...head, ...Array.from({ length: halves.length === 2 ? gap : 0 }, () => "0"), ...tail]
  if (all.some((one) => !/^[0-9a-fA-F]{1,4}$/.test(one))) {
    return null
  }

  return { six: true, value: all.reduce((sum, one) => sum * 65536n + BigInt(parseInt(one, 16)), 0n) }
}

function groups(text: string): string[] {
  return text === "" ? [] : text.split(":")
}

function writeFour(value: bigint): string {
  return [24n, 16n, 8n, 0n].map((shift) => String((value >> shift) & 255n)).join(".")
}

function writeSix(value: bigint): string {
  const words = [112n, 96n, 80n, 64n, 48n, 32n, 16n, 0n].map((shift) => Number((value >> shift) & 65535n))
  const run = zeros(words)
  const hex = words.map((one) => one.toString(16))
  if (run.length < 2) {
    return hex.join(":")
  }

  return `${hex.slice(0, run.start).join(":")}::${hex.slice(run.start + run.length).join(":")}`
}

function zeros(words: number[]): { start: number; length: number } {
  let best = { start: 0, length: 0 }
  let at = 0
  while (at < words.length) {
    let end = at
    while (end < words.length && words[end] === 0) {
      end++
    }

    if (end - at > best.length) {
      best = { start: at, length: end - at }
    }

    at = end === at ? at + 1 : end
  }

  return best
}
