export function randomKey(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(32))

  return btoa(String.fromCharCode(...bytes))
}

export function randomId(): string {
  const letters = "abcdefghijklmnopqrstuvwxyz0123456789"
  const bytes = crypto.getRandomValues(new Uint8Array(16))

  return Array.from(bytes, (one) => letters[one % letters.length]).join("")
}
