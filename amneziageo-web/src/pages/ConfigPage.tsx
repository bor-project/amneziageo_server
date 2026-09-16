import { Navigate, useNavigate, useParams } from "react-router-dom"
import { draftOf, useAddConfig, useChangeConfig, useConfigs, useFreshConfig } from "@/api/configs"
import type { Config } from "@/api/configs"
import { ConfigForm } from "@/components/ConfigForm"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { configTrail } from "@/pages/trails"

export function ConfigPage() {
  const { configId } = useParams()

  return configId === undefined ? <NewConfig /> : <HeldConfig configId={Number(configId)} />
}

function NewConfig() {
  const t = useText()
  const navigate = useNavigate()
  const configs = useConfigs()
  const fresh = useFreshConfig(configs.data !== undefined, nextName(configs.data))
  const add = useAddConfig()

  useTail([{ label: t("configs.newTitle") }])

  if (fresh.data === undefined) {
    return <div className="mt-4 text-sm text-muted">{t("configs.loading")}</div>
  }

  return (
    <ConfigForm
      start={draftOf(fresh.data)}
      publicKey={fresh.data.publicKey}
      pending={add.isPending}
      error={add.error}
      onSave={(draft) => void add.mutateAsync(draft).then((made) => navigate(`/connections/interfaces/${made.id}`))}
      onClose={() => navigate("/connections/interfaces")}
      importable
    />
  )
}

function HeldConfig({ configId }: { configId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const configs = useConfigs()
  const change = useChangeConfig()
  const all = configs.data ?? []
  const held = all.find((one) => one.id === configId)

  useTail(held === undefined ? [] : [configTrail(held, all), { label: t("configs.edit") }])

  if (held === undefined) {
    return configs.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("configs.loading")}</div>
    ) : (
      <Navigate to="/connections/interfaces" replace />
    )
  }

  const card = `/connections/interfaces/${held.id}`

  return (
    <ConfigForm
      start={draftOf(held)}
      publicKey={held.publicKey}
      pending={change.isPending}
      error={change.error}
      onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(card))}
      onClose={() => navigate(card)}
    />
  )
}

function nextName(configs: Config[] | undefined): string {
  const taken = new Set((configs ?? []).map((one) => one.name))
  for (let number = 1; number < 100; number++) {
    if (!taken.has(`awg${number}`)) {
      return `awg${number}`
    }
  }

  return "awg0"
}
