# Theme and language of the panel

The theme is the icon beside the name in the sidebar, and it walks auto, light, dark and back with every
press. The language is in `Panel settings` -> `General`. The sign-in page carries both above the form,
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
