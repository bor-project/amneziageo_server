import { useRef, useState } from "react"
import type { ReactNode } from "react"
import { useQueryClient } from "@tanstack/react-query"
import { useNavigate, useParams, useSearchParams } from "react-router-dom"
import { complaint } from "@/api/auth"
import {
  changeRelease,
  channelWords,
  channels,
  createRelease,
  dropReleaseFile,
  fileFault,
  isVersion,
  putReleaseArchive,
  putReleaseFile,
  shownTag,
  slotsOf,
  useClientRelease,
} from "@/api/clientUpdates"
import type { FileFault, ReleaseChannel, ReleaseDetails, ReleaseSlot } from "@/api/clientUpdates"
import { useFailure } from "@/api/failure"
import { useTail } from "@/components/crumbs"
import { Line, Part, Pick, Shown } from "@/components/fields"
import { card, fieldBox, footer, primary, quiet, secondary } from "@/components/styles"
import { bytes } from "@/format"
import { useText } from "@/i18n"
import type { Text, TextKey } from "@/i18n"
import { useSpot } from "@/store/spots"

const archiveKey = "archive"

const faultWords: Record<string, TextKey> = {
  format: "releases.notFormat",
  arch: "releases.builtFor",
  package: "releases.packageIs",
  control: "releases.noControl",
  libraries: "releases.noLibraries",
  unreadable: "releases.unreadable",
}

export function ClientReleasePage() {
  const t = useText()
  const { tag } = useParams()
  const release = useClientRelease(tag)
  const failed = useFailure(release)

  if (tag !== undefined && release.data === undefined) {
    return failed !== null ? (
      <div className="mt-4 text-sm text-alarm">{t(complaint(failed))}</div>
    ) : (
      <div className="mt-4 text-sm text-muted">{t("releases.loading")}</div>
    )
  }

  return <ReleaseForm key={tag ?? ""} held={release.data ?? null} />
}

function ReleaseForm({ held }: { held: ReleaseDetails | null }) {
  const t = useText()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const back = useSpot("/settings/client-updates")
  const [params] = useSearchParams()
  const [made, setMade] = useState<ReleaseDetails | null>(held)
  const [version, setVersion] = useState(held?.release.version ?? "")
  const [channel, setChannel] = useState<ReleaseChannel>(held?.release.channel ?? "stable")
  const [description, setDescription] = useState(held?.release.description ?? "")
  const [picked, setPicked] = useState<Record<number, File>>({})
  const [from, setFrom] = useState<Record<string, string>>(() => startsOf(held))
  const [dropped, setDropped] = useState<string[]>([])
  const [archive, setArchive] = useState<File | null>(null)
  const [sending, setSending] = useState<Record<string, number>>({})
  const [faults, setFaults] = useState<Record<string, string>>({})
  const [fault, setFault] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const typed = isVersion(version)
  const slots = slotsOf(typed ? version : "x.x.x.x")
  const first = params.get("archive") === "1"

  useTail([{ label: made === null ? t("releases.newTitle") : shownTag(made.release.tag) }])

  const wrong = slots.map((slot, at) => nameFault(t, slot, picked[at]) ?? fromFault(t, slot, from[slot.name]))
  const archiveWrong = archive !== null && !archive.name.toLowerCase().endsWith(".zip")
  const change = made === null ? null : changeOf(made, channel, description, from, slots, picked)
  const chosen = Object.keys(picked).length > 0 || archive !== null
  const edited = chosen || dropped.length > 0 || change !== null
  const ready =
    !busy && typed && wrong.every((one) => one === null) && !archiveWrong && (made === null ? chosen : edited)

  function pick(at: number, file: File | null) {
    const next = { ...picked }
    if (file === null) {
      delete next[at]
    } else {
      next[at] = file
    }

    setPicked(next)
    setFaults(without(faults, slots[at].name))
  }

  function toggleDrop(name: string) {
    setDropped(dropped.includes(name) ? dropped.filter((one) => one !== name) : [...dropped, name])
  }

  function share(name: string, value: number | null) {
    setSending((now) => {
      const next = { ...now }
      if (value === null) {
        delete next[name]
      } else {
        next[name] = value
      }

      return next
    })
  }

  // Makes the release or changes it, takes the files it drops out, then hands the panel the archive and every file
  // chosen, one after another; a file the panel refuses stays chosen with the reason beside it.
  async function confirm() {
    setBusy(true)
    setFault(null)
    setFaults({})
    const refused: Record<string, string> = {}
    let now = made
    let broke = false
    try {
      if (now === null) {
        now = await createRelease(version, channel, description.trim())
        setMade(now)
      } else if (change !== null) {
        now = await changeRelease(now.release.tag, change)
        setMade(now)
      }

      for (const name of dropped) {
        now = await dropReleaseFile(now.release.tag, name)
      }

      setDropped([])
      if (archive !== null) {
        try {
          now = await putReleaseArchive(now.release.tag, archive, (value) => share(archiveKey, value))
          setArchive(null)
        } catch (error) {
          refused[archiveKey] = refusal(t, error)
        } finally {
          share(archiveKey, null)
        }
      }

      const left = { ...picked }
      for (const [key, file] of Object.entries(picked)) {
        const slot = slots[Number(key)]
        try {
          now = await putReleaseFile(now.release.tag, slot.name, file, from[slot.name] ?? "", (value) =>
            share(slot.name, value),
          )
          delete left[Number(key)]
        } catch (error) {
          refused[slot.name] = refusal(t, error)
        } finally {
          share(slot.name, null)
        }
      }

      setPicked(left)
      setMade(now)
      setFaults(refused)
    } catch (error) {
      broke = true
      setFault(refusal(t, error))
    } finally {
      setBusy(false)
      await queryClient.invalidateQueries({ queryKey: ["client-releases"] })
      await queryClient.invalidateQueries({ queryKey: ["client-updates"] })
    }

    if (!broke && Object.keys(refused).length === 0) {
      navigate(back)
    }
  }

  const archivePart = (
    <Part title={t("releases.archiveLine")}>
      <div className="flex flex-wrap items-center gap-2 sm:col-span-2">
        <Chooser
          caption={t("releases.pickArchive")}
          accept=".zip,application/zip"
          disabled={busy || !typed}
          onPick={(file) => {
            setArchive(file)
            setFaults(without(faults, archiveKey))
          }}
        />
        <Clear title={t("releases.clear")} disabled={busy || archive === null} onClick={() => setArchive(null)} />
        {archive !== null && <span className="min-w-0 truncate font-mono text-xs text-ink">{archive.name}</span>}
        <State
          sending={sending[archiveKey]}
          wrong={archiveWrong ? t("releases.notZip") : (faults[archiveKey] ?? null)}
          ok={archive !== null ? bytes(t, archive.size) : null}
        />
      </div>
    </Part>
  )

  return (
    <div className="mt-4 flex max-w-[60rem] flex-col gap-4">
      <Part title={t("releases.partRelease")}>
        {made === null ? (
          <Line
            id="release-version"
            caption={t("releases.version")}
            value={version}
            placeholder="1.10.16.0"
            onChange={(value) => setVersion(value.trim())}
            fault={version.length > 0 && !typed ? t("releases.badVersion") : ""}
          />
        ) : (
          <Shown id="release-version" caption={t("releases.version")} value={made.release.version} />
        )}

        <Pick
          id="release-channel"
          caption={t("releases.channel")}
          value={channel}
          onChange={(value) => setChannel(value as ReleaseChannel)}
        >
          {channels.map((one) => (
            <option key={one} value={one}>
              {t(channelWords[one])}
            </option>
          ))}
        </Pick>

        <Line
          id="release-description"
          caption={t("releases.description")}
          value={description}
          onChange={setDescription}
          wide
        />
      </Part>

      {first && archivePart}

      <div className={`p-4.5 ${card}`}>
        <div className="text-sm font-semibold text-ink-soft">{t("releases.partFiles")}</div>
        <div className="mt-3 flex flex-col divide-y divide-line">
          {slots.map((slot, at) => {
            const kept = made?.files.find((one) => one.name === slot.name)
            const file = picked[at]
            const gone = kept !== undefined && dropped.includes(slot.name)

            return (
              <div
                key={slot.name}
                className="flex flex-wrap items-center gap-x-3 gap-y-1.5 py-2 md:grid md:grid-cols-[minmax(0,1fr)_auto_11rem_13rem]"
              >
                <span
                  className={`min-w-0 basis-full truncate font-mono text-xs ${gone ? "text-faint line-through" : "text-ink"}`}
                  title={slot.name}
                >
                  {slot.name}
                </span>
                <div className="flex items-center gap-2">
                  <Chooser
                    caption={t("releases.pick")}
                    accept={extensionOf(slot.name)}
                    disabled={busy || !typed || archive !== null}
                    onPick={(one) => pick(at, one)}
                  />
                  <Clear
                    title={t("releases.clear")}
                    disabled={busy || (file === undefined && kept === undefined)}
                    onClick={() => (file !== undefined ? pick(at, null) : toggleDrop(slot.name))}
                  />
                </div>
                {slot.installer === null || (file === undefined && kept === undefined) ? (
                  <span className="max-md:hidden" />
                ) : (
                  <label className="flex items-center gap-1.5 text-xs text-muted" htmlFor={`release-from-${at}`}>
                    {t("releases.from")}
                    <input
                      id={`release-from-${at}`}
                      value={from[slot.name] ?? ""}
                      placeholder="1.10.9.0"
                      disabled={busy}
                      onChange={(e) => setFrom({ ...from, [slot.name]: e.target.value.trim() })}
                      className={`w-28 py-1.5 ${fieldBox}`}
                    />
                  </label>
                )}
                <div className="text-xs">
                  <State
                    sending={sending[slot.name]}
                    wrong={wrong[at] ?? faults[slot.name] ?? null}
                    ok={file !== undefined ? bytes(t, file.size) : kept !== undefined && !gone ? bytes(t, kept.size) : null}
                    gone={gone ? t("releases.dropping") : null}
                  />
                </div>
              </div>
            )
          })}
        </div>
      </div>

      {!first && archivePart}

      {fault !== null && <div className="text-sm text-alarm">{fault}</div>}

      <div className={footer}>
        <button type="button" className={secondary} disabled={busy} onClick={() => navigate(back)}>
          {t("settings.cancel")}
        </button>
        <button type="button" className={primary} disabled={!ready} onClick={() => void confirm()}>
          {busy ? t("releases.busy") : t("releases.confirm")}
        </button>
      </div>
    </div>
  )
}

// A button that opens the picker of a file.
function Chooser({
  caption,
  accept,
  disabled,
  onPick,
}: {
  caption: string
  accept: string
  disabled: boolean
  onPick: (file: File) => void
}) {
  const input = useRef<HTMLInputElement>(null)

  return (
    <>
      <button type="button" className={`${secondary} py-1.5`} disabled={disabled} onClick={() => input.current?.click()}>
        {caption}
      </button>
      <input
        ref={input}
        type="file"
        accept={accept}
        className="hidden"
        onChange={(event) => {
          const one = event.target.files?.[0]
          event.target.value = ""
          if (one !== undefined) {
            onPick(one)
          }
        }}
      />
    </>
  )
}

function Clear({ title, disabled, onClick }: { title: string; disabled: boolean; onClick: () => void }) {
  return (
    <button type="button" title={title} aria-label={title} className={quiet} disabled={disabled} onClick={onClick}>
      &times;
    </button>
  )
}

// What became of a file: on its way, refused, taken, or to be taken out.
function State({
  sending,
  wrong,
  ok,
  gone = null,
}: {
  sending: number | undefined
  wrong: string | null
  ok: string | null
  gone?: string | null
}): ReactNode {
  const t = useText()
  if (sending !== undefined) {
    return <span className="text-muted">{t("releases.sending", { percent: Math.round(sending * 100) })}</span>
  }

  if (wrong !== null) {
    return <span className="text-alarm">{`✗ ${wrong}`}</span>
  }

  if (gone !== null) {
    return <span className="text-muted">{gone}</span>
  }

  if (ok !== null) {
    return (
      <span className="text-muted">
        <span className="text-good">✓</span> {ok}
      </span>
    )
  }

  return null
}

function nameFault(t: Text, slot: ReleaseSlot, file: File | undefined): string | null {
  return file === undefined || file.name === slot.name ? null : t("releases.badName", { name: file.name })
}

function fromFault(t: Text, slot: ReleaseSlot, from: string | undefined): string | null {
  return slot.installer === null || from === undefined || from.length === 0 || isVersion(from)
    ? null
    : t("releases.badFrom")
}

// The words a refusal of the panel comes to: what is wrong with the file when the panel looked into it.
function refusal(t: Text, error: unknown): string {
  const file = fileFault(error)
  if (file !== null) {
    return faultText(t, file)
  }

  return t(complaint(error))
}

function faultText(t: Text, file: FileFault): string {
  const word = faultWords[file.reason]

  return word ? t(word, { found: file.found, wanted: file.wanted }) : t("error.badReleaseFile")
}

// What changes about a release that is there: its channel, its description and where its patches start.
function changeOf(
  made: ReleaseDetails,
  channel: ReleaseChannel,
  description: string,
  from: Record<string, string>,
  slots: ReleaseSlot[],
  picked: Record<number, File>,
) {
  const sent = new Set(Object.keys(picked).map((key) => slots[Number(key)].name))
  const starts: Record<string, string> = {}
  for (const file of made.files) {
    const now = from[file.name] ?? ""
    if (file.name.endsWith(".patch") && !sent.has(file.name) && now !== file.from) {
      starts[file.name] = now
    }
  }

  const said = description.trim()
  const moved = channel !== made.release.channel
  const told = said !== made.release.description
  const started = Object.keys(starts).length > 0
  if (!moved && !told && !started) {
    return null
  }

  return {
    ...(moved ? { channel } : {}),
    ...(told ? { description: said } : {}),
    ...(started ? { from: starts } : {}),
  }
}

function startsOf(held: ReleaseDetails | null): Record<string, string> {
  const starts: Record<string, string> = {}
  for (const file of held?.files ?? []) {
    if (file.from.length > 0) {
      starts[file.name] = file.from
    }
  }

  return starts
}

function extensionOf(name: string): string {
  const dot = name.lastIndexOf(".")

  return dot < 0 ? "" : name.slice(dot)
}

function without(faults: Record<string, string>, name: string): Record<string, string> {
  const next = { ...faults }
  delete next[name]

  return next
}
