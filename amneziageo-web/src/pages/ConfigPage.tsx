import { useState } from "react"
import { Navigate, useLocation, useNavigate, useParams } from "react-router-dom"
import { downedOf, draftOf, failure, useAddConfig, useChangeConfig, useConfigs, useFreshConfig } from "@/api/configs"
import type { Config } from "@/api/configs"
import { scopes } from "@/api/scopes"
import { ConfigForm } from "@/components/ConfigForm"
import { useTail } from "@/components/crumbs"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"
import { useSpot } from "@/store/spots"

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
  const back = useSpot("/connections/interfaces")

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
      onSave={(draft) =>
        void add.mutateAsync(draft).then(
          () => navigate(back),
          (error: unknown) => {
            const downed = downedOf(error)
            if (downed !== null) {
              navigate(`/connections/interfaces/${downed.id}/edit`, {
                state: { fault: failure(t, error), of: downed.error },
              })
            }
          },
        )
      }
      onClose={() => navigate(back)}
      importable
    />
  )
}

function HeldConfig({ configId }: { configId: number }) {
  const t = useText()
  const navigate = useNavigate()
  const user = useAppSelector((s) => s.auth.user)
  const { state } = useLocation()
  const configs = useConfigs()
  const change = useChangeConfig()
  const [round, setRound] = useState(0)
  const may = holds(user, scopes.manageInterfaces)
  const arrived = (state as { fault?: string } | null)?.fault ?? ""
  const arrivedOf = (state as { of?: string } | null)?.of ?? ""
  const all = configs.data ?? []
  const held = all.find((one) => one.id === configId)
  const back = useSpot("/connections/interfaces")

  useTail(held === undefined ? [] : [{ label: held.name }])

  if (held === undefined) {
    return configs.data === undefined ? (
      <div className="mt-4 text-sm text-muted">{t("configs.loading")}</div>
    ) : (
      <Navigate to={back} replace />
    )
  }

  return (
    <ConfigForm
      key={round}
      start={draftOf(held)}
      publicKey={held.publicKey}
      self={held.id}
      pending={change.isPending}
      error={change.error}
      fault={change.error === null ? arrived : ""}
      faultOf={change.error === null ? arrivedOf : ""}
      onSave={(draft) =>
        void change.mutateAsync({ id: held.id, draft }).then(
          () => navigate(back),
          (error: unknown) => {
            if (downedOf(error) !== null) {
              setRound((one) => one + 1)
            }
          },
        )
      }
      onClose={() => navigate(back)}
      onRemove={may ? () => navigate(`/connections/interfaces/${held.id}/delete`) : undefined}
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
