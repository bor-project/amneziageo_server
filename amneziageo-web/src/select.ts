interface Chord {
  ctrlKey: boolean
  metaKey: boolean
  shiftKey: boolean
  altKey: boolean
  code: string
}

export function selectsAll(event: Chord): boolean {
  return (event.ctrlKey || event.metaKey) && !event.shiftKey && !event.altKey && event.code === "KeyA"
}

export function selectText(node: Node) {
  window.getSelection()?.selectAllChildren(node)
}
