# FF7 binary/API/browser acceptance

This project sends authored FF7 containers through the real ZIP multipart importer,
plugin catalog, Probe/Open gates, session, preset library and F6 planner. It does not
replace application responses or construct a gameplay document directly.

`../fixtures/SaveToolkit.Ff7BrowserFixture` composes the public, test-only
`Ff7AcceptanceFixtures.Create(titleId)` bridge in the FF7 test assembly. The plugin
owns its writer, layout and catalog choices. The framework helper owns ZIP framing,
in-memory destination mutations and browser metadata. It exposes no production API.

The bridge authors Remake (9,434,752 bytes), Rebirth and Demo (23,381,104 bytes each),
plus each title's untouched 1,056-byte common companion. These are synthetic linked
containers. Parsing, serialization, reimport and complete member comparisons are
the integrity oracles; this coverage establishes neither console authenticity nor
native game load/resave behavior, and does not assert a gameplay checksum exists.

The API matrix covers all five typed actions, each advertised scope, explicit empty
Selected, character identity, current inventory candidates, moved owned materia,
malformed joins, compatibility, malformed containers, payload tampering, stale
plans and failed storage. Failure snapshots compare complete exported member bytes,
revision, Undo/Redo and the stored library. Apply cases assert one F6 history group,
Undo/Redo, untouched companion bytes and actual reimport. The level action checks
the exact primary bytes and the plan's derived XP field; the plugin tests own the
independent game-curve oracle.

Configure scratch as described in [RELEASING](../../docs/RELEASING.md#disk-and-scratch),
then run from the repository root:

```powershell
./scripts/test.ps1 -Filter SaveToolkit.Ff7Browser.Tests -Configuration Release -MaxParallel 1 -ResultsDirectory <scratch>/api-results
```

`SAVETOOLKIT_FF7_EVIDENCE` optionally selects a separate evidence root. Each process
creates its own child directory, retaining unchanged HTTP request/response JSON,
whole exported ZIPs and member SHA-256 manifests. Only its expanded application
storage is removed. Do not point scratch or artifacts at private saves.

The browser cases in `web/e2e/ff7-*.e2e.ts` use the normal UI file input, actual
action forms and row selections. They cover four viewport widths, library CRUD,
preview cancellation, explicit ambiguity, selected characters, moved owned materia,
storage/read-only/stale states, keyboard controls and whole ZIP download/reimport.
They retain traces, screenshots and unchanged API JSON. Generated fixtures live in
the invocation's scratch tree, never in the repository.

Run the single official browser entry from `web` after building the Release API/UI:

```powershell
npm run e2e
```

The runner verifies that selected test identities partition into canonical, real
Unreal and real FF7 suites. All three suites are mandatory in an unfiltered run;
each keeps its 180-second global limit, 30-second test limit, one worker and zero
retries. A focused developer invocation such as `npm run e2e -- ff7-history` retains
the same discovery, reporting and teardown behavior. Browser success is claimed
only for an executed run, never from API-only results or fixture generation.
