import type { Proxy } from "@/api/proxies"
import { useText } from "@/i18n"

export function ProxyState({ one, fault }: { one: Proxy; fault: string }) {
  const t = useText()

  if (fault.length > 0) {
    return (
      <span className="text-alarm" title={fault}>
        {t("proxies.stopped")}
      </span>
    )
  }

  return (
    <span className={one.isRunning ? "text-good" : "text-muted"}>
      {one.isRunning ? t("proxies.running") : t("proxies.stopped")}
    </span>
  )
}
