export const defaultName = "{HOST}-{INTERFACE}-{CLIENT}"

export const nameKeys = ["HOST", "INTERFACE", "CLIENT", "ID", "PORT", "NOTE", "DATE"]

export const defaultProxyName = "{PROXY}-{HOST}-{TRANSPORT}"

export const proxyNameKeys = ["HOST", "PROXY", "TRANSPORT", "ID", "PORT", "PATH"]

const separators = "-_.| "

interface Part {
  text: string
  key: string | null
}

export function unknownKeys(template: string, keys = nameKeys): string[] {
  return parts(template)
    .filter((part) => part.key !== null && known(part.key, keys) === null)
    .map((part) => part.text)
}

export function fillName(template: string, values: Record<string, string>, stamp: string, keys = nameKeys): string {
  let name = ""
  let dropping = false
  for (const part of parts(template)) {
    const key = part.key === null ? null : known(part.key, keys)
    if (key !== null) {
      const value = (values[key] ?? "").trim()
      if (value.length === 0) {
        dropping = true
        continue
      }

      name += value
      dropping = false
      continue
    }

    name += dropping && separators.includes(part.text[0]) ? part.text.slice(1) : part.text
    dropping = false
  }

  const line = [...name].filter((letter) => !control(letter)).join("")
  const trimmed = edges(line)

  return trimmed.length > 0 ? trimmed : stamp
}

export function fillProxyName(template: string, values: Record<string, string>, stamp: string): string {
  const original = values.PROXY ?? stamp
  const name = template === "{PROXY}" ? original : fillName(template, values, original, proxyNameKeys)
  let length = Math.min(name.length, 64)
  const last = name.charCodeAt(length - 1)
  if (length < name.length && last >= 0xd800 && last <= 0xdbff) {
    length--
  }

  return name.slice(0, length)
}

function parts(template: string): Part[] {
  const found: Part[] = []
  let at = 0
  for (const match of template.matchAll(/\{([^{}]*)\}/g)) {
    if (match.index > at) {
      found.push({ text: template.slice(at, match.index), key: null })
    }

    found.push({ text: match[0], key: match[1] })
    at = match.index + match[0].length
  }

  if (at < template.length) {
    found.push({ text: template.slice(at), key: null })
  }

  return found
}

function known(key: string, keys: string[]): string | null {
  const bare = key.trim().toUpperCase()

  return keys.find((one) => one === bare) ?? null
}

function control(letter: string): boolean {
  const code = letter.charCodeAt(0)

  return code < 0x20 || (code >= 0x7f && code <= 0x9f)
}

function edges(text: string): string {
  let start = 0
  let end = text.length
  while (start < end && separators.includes(text[start])) {
    start++
  }

  while (end > start && separators.includes(text[end - 1])) {
    end--
  }

  return text.slice(start, end)
}
