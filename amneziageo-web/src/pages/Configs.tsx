import { useState } from "react"
import {
  draftOf,
  useAddConfig,
  useApplyConfig,
  useChangeConfig,
  useConfigs,
  useFreshConfig,
  useRemoveConfig,
} from "@/api/configs"
import type { Config } from "@/api/configs"
import { scopes } from "@/api/scopes"
import { ConfigForm } from "@/components/ConfigForm"
import { Modal } from "@/components/Modal"
import { RowActions } from "@/components/RowActions"
import { Rows } from "@/components/Rows"
import { card, danger, primary, secondary } from "@/components/styles"
import { useText } from "@/i18n"
import { holds } from "@/store/authSlice"
import { useAppSelector } from "@/store/hooks"

export function Configs() {
  const t = useText()
  const user = useAppSelector((s) => s.auth.user)
  const configs = useConfigs()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<Config | null>(null)
  const [removing, setRemoving] = useState<Config | null>(null)
  const fresh = useFreshConfig(adding, nextName(configs.data))
  const add = useAddConfig()
  const change = useChangeConfig()
  const remove = useRemoveConfig()
  const apply = useApplyConfig()
  const may = holds(user, scopes.manageInterfaces)

  async function save(config: Config, draft: Parameters<typeof change.mutateAsync>[0]["draft"]) {
    await change.mutateAsync({ id: config.id, draft })
    setEditing(null)
  }

  return (
    <div>
      <div className={`mt-4 ${card}`}>
        {may && (
          <div className="flex justify-end border-b border-line px-4 py-3">
            <button type="button" onClick={() => setAdding(true)} className={primary}>
              {t("configs.add")}
            </button>
          </div>
        )}

        {configs.data?.length === 0 && <div className="px-4 py-6 text-sm text-muted">{t("configs.empty")}</div>}

        {configs.data && configs.data.length > 0 && (
          <Rows
            items={configs.data}
            keyOf={(config) => config.id}
            columns={[
              {
                key: "name",
                caption: t("configs.name"),
                sort: (config) => config.name,
                lead: true,
                body: "font-medium text-ink",
                cell: (config) => config.name,
              },
              {
                key: "endpoint",
                caption: t("configs.endpoint"),
                sort: (config) => (config.host.length > 0 ? `${config.host}:${config.listenPort}` : config.listenPort),
                body: "text-muted",
                cell: (config) =>
                  config.host.length > 0 ? `${config.host}:${config.listenPort}` : config.listenPort,
              },
              {
                key: "address",
                caption: t("configs.address"),
                sort: (config) => config.address.join(", "),
                body: "text-muted",
                cell: (config) => config.address.join(", "),
              },
              {
                key: "public",
                caption: t("configs.public"),
                sort: (config) => config.publicKey,
                body: "max-w-56 text-muted",
                cell: (config) => <span className="block truncate">{config.publicKey}</span>,
              },
              {
                key: "actions",
                caption: t("configs.actions"),
                tail: true,
                cell: (config) =>
                  may && (
                    <RowActions
                      title={t("configs.actions")}
                      actions={[
                        { label: t("configs.edit"), onPick: () => setEditing(config) },
                        { label: t("configs.apply"), onPick: () => void apply.mutateAsync(config.id) },
                        { label: t("configs.remove"), onPick: () => setRemoving(config), alarming: true },
                      ]}
                    />
                  ),
              },
            ]}
          />
        )}
      </div>

      {adding && fresh.data && (
        <ConfigForm
          title={t("configs.newTitle")}
          start={draftOf(fresh.data)}
          publicKey={fresh.data.publicKey}
          pending={add.isPending}
          error={add.error}
          onSave={(draft) => void add.mutateAsync(draft).then(() => setAdding(false))}
          onClose={() => setAdding(false)}
          importable
        />
      )}

      {editing && (
        <ConfigForm
          title={t("configs.editTitle", { name: editing.name })}
          start={draftOf(editing)}
          publicKey={editing.publicKey}
          pending={change.isPending}
          error={change.error}
          onSave={(draft) => void save(editing, draft)}
          onClose={() => setEditing(null)}
        />
      )}

      {removing && (
        <Modal
          title={t("configs.removeTitle", { name: removing.name })}
          onClose={() => setRemoving(null)}
          footer={
            <>
              <button type="button" onClick={() => setRemoving(null)} className={secondary}>
                {t("configs.cancel")}
              </button>
              <button
                type="button"
                onClick={() => void remove.mutateAsync(removing.id).then(() => setRemoving(null))}
                disabled={remove.isPending}
                className={danger}
              >
                {t("configs.remove")}
              </button>
            </>
          }
        >
          <div className="text-sm text-muted">{removing.name}</div>
        </Modal>
      )}
    </div>
  )
}

function nextName(configs: Config[] | undefined): string {
  const taken = new Set((configs ?? []).map((config) => config.name))
  for (let number = 1; number < 100; number++) {
    if (!taken.has(`awg${number}`)) {
      return `awg${number}`
    }
  }

  return "awg0"
}
