import { useSearchParams } from "react-router-dom"

export function useRoutingEditor() {
  const [params, setParams] = useSearchParams()
  const selected = params.get("edit")
  const id = selected !== null && selected !== "new" && /^-?\d+$/.test(selected) ? Number(selected) : null

  function put(key: string, value: string | null) {
    const next = new URLSearchParams(params)
    if (value === null || value === "") {
      next.delete(key)
    } else {
      next.set(key, value)
    }

    setParams(next, { replace: true })
  }

  return {
    params,
    id,
    fresh: selected === "new",
    open: (id: number | "new") => put("edit", String(id)),
    close: () => put("edit", null),
    filter: put,
  }
}
