# Synthetic browser fixtures

Every file here is hand-authored test data. None comes from a game save, account,
private sample, or downloaded tool. Tests create ZIP archives in memory with a fixed
timestamp; corrupt ZIP cases derive from those archives or the companion text.

- `save.json`: scalars, nested values and a small array, with intentional whitespace,
  escapes and numeric spelling for byte-preservation checks.
- `companion.txt`: an untouched payload and a non-ZIP upload disguised as `.zip`.
- `profile.json`: a second independently editable document with different coins.
- `queryable.json`: 60 numbered fields, two strings, one boolean and the root object
  (64 searchable nodes). This crosses the UI's 50-row page boundary.
- `unknown.txt`: unrecognized content imported under an unknown directory layout.

Run against the Release API and built UI with `npm run e2e` from `web/`, following
the setup in `docs/RELEASING.md`. Set `SAVETOOLKIT_E2E_SCRATCH` and
`PLAYWRIGHT_BROWSERS_PATH` to put temporary workspaces, browser profiles, reports
and downloads outside the checkout. Only Chromium runs; no private samples are used.

Export comparisons cover every path and decompressed payload byte. ZIP timestamps
and compression are packaging metadata, so whole-archive bytes are not compared
(see the export contract in `docs/API.md`).
