# Translating the docs

The docs site text lives in one JSON file per language, like the web UI's `web/src/i18n/en.json`.

## Adding or updating a translation

1. Copy `en.json` to `<lang>.json` (e.g. `ru.json`), or open the existing file for your language.
2. Translate the values. Keep the keys as they are.
3. Send a pull request with just that JSON file.

You don't need to touch any Markdown. The pages under `docs/_<lang>/` are generated from your file,
with the same screenshots, links and layout as the English pages.

Tips:

- Values are Markdown. Keep `**bold**`, `` `code` `` and links like `[Overview](overview.html)` as they
  are; only translate the visible text (`Overview`). Link targets are fixed up for your language
  automatically.
- A key you leave out shows the English text until it's translated.
- Keys are grouped per page (`progress/raid-prep`) and per section (`map.p1` is the first paragraph
  under the "Map" heading), so you can find the matching English text quickly.

## For maintainers

```
node tools/docs-i18n.mjs build            # regenerate en.json and docs/_<lang>/ pages
node tools/docs-i18n.mjs check            # same, but only reports; exits 1 if anything is out of date
node tools/docs-i18n.mjs accept <lang> [page:key...]
```

- Edit the English pages, then run `build`. It reports, per language, keys that are **missing**,
  **stale** (the English text changed after the translation was made) and **unused** (no longer in
  `en.json`).
- A stale key clears itself once its translation is edited. If the old translation still fits, run
  `accept`.
- `<lang>.sync.json` is managed by the script; don't edit it.
- A new language also needs its collection and `note-<lang>` / `warning-<lang>` callouts in
  `docs/_config.yml`, plus a branch in the language switch and sidebar includes.
- Korean (`docs/_kr/`) is written by hand and isn't generated.
