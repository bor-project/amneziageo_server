export const card = "rounded-xl border border-line bg-surface"

// The bar with the buttons of a form, kept in sight at the bottom while the form scrolls; its second shadow covers
// what passes under it in the padding of the page.
export const footer =
  "sticky bottom-0 z-10 flex justify-end gap-2 rounded-xl border border-line bg-surface px-4 py-3.5 shadow-[0_-10px_24px_-18px_rgba(15,23,42,.35),0_26px_0_6px_var(--canvas)]"

export const field =
  "w-full rounded-lg border border-line-input bg-input px-3 py-2.25 text-sm text-ink outline-none focus:border-brand disabled:opacity-50"

// The same as field without w-full, for fields with an explicit width in a flex row.
export const fieldBox =
  "rounded-lg border border-line-input bg-input px-3 py-2.25 text-sm text-ink outline-none focus:border-brand disabled:opacity-50"

export const primary =
  "rounded-lg bg-brand px-4.5 py-2.5 text-sm font-medium text-white hover:bg-brand-hover disabled:opacity-50"

export const secondary =
  "rounded-lg border border-line-button px-4 py-2.25 text-sm text-ink-soft hover:bg-active disabled:opacity-50"

export const danger =
  "rounded-lg bg-alarm-button px-4.5 py-2.5 text-sm font-medium text-white hover:bg-alarm-button-hover disabled:opacity-50"

export const quiet = "rounded-md px-2 py-1 text-sm text-muted hover:bg-active hover:text-ink"

// A compact button on a bar over a list.
export const tool =
  "inline-flex h-8 items-center gap-1.5 rounded-lg border border-line-button bg-surface px-3 text-[13px] text-ink-soft hover:bg-hover hover:text-ink disabled:opacity-50"

export const toolRisky =
  "inline-flex h-8 items-center gap-1.5 rounded-lg border border-alarm-line bg-surface px-3 text-[13px] text-alarm hover:bg-alarm-soft disabled:opacity-50"

export const label = "block text-xs text-muted"

export const note = "mt-1 text-xs text-muted"

export const chip = "rounded-md bg-chip px-2.5 py-1.25 text-[13px] text-chip-ink"

export const menu = "rounded-[10px] border border-line-menu bg-menu p-1.5 shadow-[var(--shade)]"

export const menuItem = "rounded-md px-2.5 py-2 text-left text-[13px] text-ink-soft hover:bg-active hover:text-ink"
