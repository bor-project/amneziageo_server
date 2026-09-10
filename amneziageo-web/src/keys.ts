export function randomKey(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(32))

  return btoa(String.fromCharCode(...bytes))
}
