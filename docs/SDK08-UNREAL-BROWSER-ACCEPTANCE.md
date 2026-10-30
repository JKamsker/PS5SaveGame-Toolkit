# Unreal canonical-reference browser acceptance

The active `unreal-reference.e2e.ts` and `unreal-presentation.e2e.ts` tests import
authored binary GVAS saves through the built application's normal ZIP picker. They
assert the actual open response selects `engine.unreal-gvas`. The server, title
schema detection, field metadata, reference normalization, views, mutations, history,
semantic review and export are production paths. No reference DTO is manufactured
or substituted. One recovery case aborts collection GETs at the transport layer,
then lets the normal request reach the real server on manual retry.

## Synthetic inputs

`tests/fixtures/SaveToolkit.UnrealBrowserFixture` composes the existing pure
`GvasFixture.cs` binary writer through an MSBuild linked source file. It writes
only to the absolute scratch directory supplied by the browser test. It does not
load private samples, use a permanent test endpoint or add production special cases.
The test project `tests/SaveToolkit.UnrealBrowser.Tests` verifies the fixtures
against the actual plugin before browser use.

The Jedi settings header and `HDRBrightness` field match the authored
`TitleFixture.Settings` example and the exact title gates in `UnrealGvasSchema`.
The Sackboy nested `CollectabellsStored`, opaque row and protected checksum match
the authored `GvasFieldReferenceTests` example. The Jedi actor fixture uses the
same authored compressed `ActorRecords` stream as `TitleFixture`. The wide fixture
uses the Jedi settings schema with 200 authored strings of 4,096 Unicode characters
plus a unique tail, so it exercises the title consumer's text fallback rather than
the generic SaveDiff compatibility path.

These are parser-valid synthetic saves, not native game captures. Storage-format
and host/browser evidence does not establish gameplay effects, checksum coverage
or console acceptance.

## Checks

- The same canonical scalar appears in eager Search, raw `gvas/properties` and
  named `jedi/settings` views, with identical values, confidence and warnings.
- Keyboard edits, local invalid-draft cancellation, current-value review, one
  Undo and one Redo use the real backend. An unchanged browser draft sends no edit
  and preserves history/revision. Direct repeated API submissions also preserve
  revision/history when clean, dirty and after Undo, with exact exports at each stage.
  The post-Undo request retains Redo. A mixed repeated/effective API batch keeps
  its reported rows and one undo entry, with exact edited/undone/redone exports.
  Review labels, coverage and Jump/Revert availability are checked against the actual
  response, so future provenance/navigation enrichment can pass without preserving
  an old fallback. The initial PR66 run uses raw-ID labels, `complete=true` and
  disabled Jump/Revert; metadata-backed review navigation is not claimed at that head.
- Unchanged, undone, edited and reimported exports compare all uncompressed paths
  and payload bytes, including an untouched companion. The expected edit is generated
  independently from the writer with only the intended float changed; a fixture
  test checks that it changes exactly one payload byte.
- Sackboy container/opaque summaries, inferred warnings and checksum write rejection
  remain honest. Jedi actor observations are text; protected CRC and stream length
  metadata remain read-only.
- Wide values remain bounded text, with an explicit ellipsis after 192 characters.
  Their undisplayed tail matches full-model search; both raw/named host pages return
  200 rows below the 2 MiB response limit. No truncated scalar `FieldValue` is claimed.
- At 390, 768, 1024 and 1440 pixels, labelled search and field controls are keyboard
  reachable, warning details wrap within the page and screenshots are retained.

## Host corrections

Repeated scalar edits still run provider validation and setters. The host removes
an all-ineffective reported scalar batch only after typed `FieldValue` equality,
no derived rows/structure/memento, and a complete identical serialized output proof
(paths, count and bytes). The proof copies borrowed memory before mutation and is
limited to 8 MiB; larger outputs or unavailable proof keep existing history behavior.
For declared system outputs, proof and export reuse the same exact ownership-set
check: primary and every declared path must appear exactly once, with no extras.
Incomplete output cannot establish a no-op. Export/download keep the landed
gate-captured output bytes, dirty status and revision; no later live-state read
or second serialization is introduced into verification. Included clean sources
and dirty sources retain immutable primary/system fingerprint preflight.
Mixed batches, explicit unions, derived effects and structural/memento outcomes
retain their original rows and replay path. Byte differences, including signed
zero or an unreported secondary-file mutation, prevent suppression. This is not
a repair for a provider that violates atomicity or omits all mutation reports.
Reported-row membership uses one ordinal-ID lookup with canonical typed values,
so a large repeated batch does not rescan every request for every reported row.

The actual Jedi setter may mark its internal buffer dirty on a repeated write.
After a byte-identical proof, the host stays clean (or keeps its prior history);
it does not invent a memento or reset provider state. Real-plugin tests compare
the complete provider output, then exercise a later edit/Undo/Redo and export.
Warnings survive suppression. Existing API explicit-null acceptance now asserts
the unchanged value and unchanged state instead of requiring an ineffective row.

The table detail cell is bounded by its existing scroller's inline container,
with a one-column detail layout below 600 pixels. This keeps full warnings and
editors visible even when the table's column grid scrolls horizontally. Existing
bundle budgets remain unchanged.

## Run and inspect

Follow [the built-app browser workflow](RELEASING.md#built-app-browser-smoke-tests)
with a new process-owned `SAVETOOLKIT_E2E_SCRATCH` for every invocation. Build the
Release API/UI and install Chromium as documented there. Then, from `web/`:

```powershell
npm run typecheck
npm run e2e -- --trace on
# Focused developer run (the same official entry preserves file filters):
npm run e2e -- unreal-reference.e2e.ts unreal-presentation.e2e.ts --trace on
```

From the repository root:

```powershell
dotnet test --project tests/SaveToolkit.UnrealBrowser.Tests --configuration Release
```

The browser tests retain successful screenshots, actual host JSON and generator
logs as attachments. `--trace on` retains successful traces as well as failures.
The standard Playwright teardown stops its API and removes only that invocation's
workspace/reference-pack directory. Reports, downloads and synthetic inputs remain
in test output for review. The official local/CI entry partitions discovered tests
into the original canonical and added real-Unreal suites, verifies their union has
no omissions or duplicates, and runs both with separate unchanged 180-second,
one-worker, zero-retry gates. Each has a unique server/scratch/report lifecycle;
the aggregate allowance includes both mandatory suites. Hosted CI collects both
reports and discovers the fixture unit project; no private data is required.
Suite launch errors are identified and retained as aggregate failures while later
selected suites are still attempted, just like ordinary nonzero process exits.

Unresolved/deleted references are not emitted by these supported scalar fixtures.
Their UI state remains covered by separate framework/component tests; this suite
does not fabricate one to claim Unreal consumer coverage. The generic SaveDiff
text-cell boundary, every title/build, structural deletion, all export failure
states and native console behavior are outside this focused acceptance suite.
