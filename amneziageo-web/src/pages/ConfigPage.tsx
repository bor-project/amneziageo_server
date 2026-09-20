import { Navigate, useNavigate, useParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import { draftOf, useAddConfig, useApplyConfig, useChangeConfig, useConfigs, useFreshConfig } from "@/api/configs"
import type { Config } from "@/api/configs"
import { scopes } from "@/api/scopes"
import { ConfigForm } from "@/components/ConfigForm"
import { useTail } from "@/components/crumbs"
import { secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"
import { lastSpot } from "@/store/spots"

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
  const back = lastSpot("connections", "/connections/interfaces")

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
      onSave={(draft) => void add.mutateAsync(draft).then(() => navigate(back))}
      onClose={() => navigate(back)}
      importable
    />
  )
}

function HeldConfig({ configId }: { configId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const configs = useConfigs()
  const change = useChangeConfig()
  const apply = useApplyConfig()
  const may = holds(user, scopes.manageInterfaces)
  const all = configs.data ?? []
  const held = all.find((one) => one.id === configId)
  const back = lastSpot("connections", "/connections/interfaces")

  useTail(held === undefined ? [] : [{ label: held.name }, { label: t("configs.edit") }])

  if (held === undefined) {
    return configs.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("configs.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  return (
    <div>
      {may && (
        <div className="mt-4 flex justify-end">
          <button
            type="button"
            onClick={() => void apply.mutateAsync(held.id)}
            disabled={apply.isPending}
            className={secondary}
          >
            {t("configs.apply")}
          </button>
        </div>
      )}

      {apply.error !== null && <div className="mt-2 text-sm text-alarm">{t(complaint(apply.error))}</div>}

      <ConfigForm
        start={draftOf(held)}
        publicKey={held.publicKey}
        self={held.id}
        pending={change.isPending}
        error={change.error}
        onSave={(draft) => void change.mutateAsync({ id: held.id, draft }).then(() => navigate(back))}
        onClose={() => navigate(back)}
        onRemove={may ? () => navigate(`/connections/interfaces/${held.id}/delete`) : undefined}
      />
    </div>
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
