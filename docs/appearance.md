# Theme and language of the panel

The theme is the icon in the header, and it walks auto, light, dark and back with every press. The
language is in the account menu next to it. The sign-in page carries both above the form,
so a browser that speaks neither language is not stuck. The choice is kept in `localStorage` of the browser
under `amneziageo.theme` and `amneziageo.language`, so it belongs to the browser, not to the account.

| Choice | Theme | Language |
|---|---|---|
| auto | follows `prefers-color-scheme` of the system, live | follows the languages of the browser, English when neither fits |
| named | light or dark | English or Russian |

## Colours

`src/index.css` carries the whole palette. Every colour is a variable of `:root`, and `.dark` replaces the
same names; Tailwind sees them through `@theme inline`, so the utilities read the same in both themes.

| Utility | Holds |
|---|---|
| `bg-canvas` | the page under everything |
| `bg-surface` | cards, the sidebar, the header, fields |
| `text-ink` | the reading text |
| `text-muted` | labels and secondary lines |
| `border-line` | borders and rules |
| `bg-hover` | the background a control takes under the pointer |
| `bg-brand` `text-brand-ink` `bg-brand-soft` | the accent, the accent on the canvas, the accent behind an active item |
| `text-alarm` `bg-alarm-soft` | a failure and the field it sits in |

A component names only these, never a `slate-*` or a `dark:` variant, and both themes follow it on their own.

`index.html` sets the class on the root element before the first paint, so a dark browser opens the panel
dark with no flash of white.

## Widths

`src/index.css` names the breakpoints, and they are the ones Bootstrap uses.

| Name | From |
|---|---|
| `sm` | 576px |
| `md` | 768px |
| `lg` | 992px |
| `xl` | 1200px |
| `2xl` | 1400px |

`src/theme/width.ts` reads the same widths in code: `useAbove(roomyQuery)` holds from `md`, `useAbove(wideQuery)`
from `lg`, and both follow the window as it changes.

| Width | Menu | Lists | Fields of a form |
|---|---|---|---|
| under `sm` | over the page | cards | one column |
| `sm` to `md` | over the page | cards | two columns |
| `md` to `lg` | over the page | tables | two columns |
| `lg` and wider | a column of its own | tables | two columns |

The menu over the page closes on a pick, on a press outside it and on Escape.

## Lists

`src/components/Rows.tsx` prints a list: a table on a wide screen, a card for every row on a narrow one. A column
carries its caption and its cell; `lead` names the column that heads the card, `tail` the one that sits in its
corner, `head` and `body` add classes to the cells of the table. A cell that comes out empty is left out of the card.

```tsx
<Rows
  items={clients}
  keyOf={(one) => one.id}
  columns={[
    { key: "name", caption: t("clients.name"), lead: true, cell: (one) => one.name },
    { key: "address", caption: t("clients.address"), body: "text-muted", cell: (one) => one.address.join(", ") },
    { key: "actions", caption: t("clients.actions"), tail: true, cell: (one) => <RowActions title={...} actions={...} /> },
  ]}
/>
```

## Words

`src/i18n/en.ts` holds every line of the interface and, through `keyof typeof en`, the list of keys. A line
added there without a Russian one in `src/i18n/ru.ts` breaks the build.

```tsx
const t = useText()

t("nav.clients")
t("health.version", { version: "1.0.0.0" })
```

`{name}` in a line is replaced by the value of the same name.

The server answers a failure with a code, not with a sentence: `api/auth.ts` turns the code into a key and
the page prints it in the chosen language.
