# Framework mutation testing (#40)

Restore the pinned local tool with `dotnet tool restore`, then run from the checkout root:

```powershell
./tests/Invoke-Mutations.ps1 -Area Export -Label after
./tests/Invoke-Mutations.ps1 -Area Editing,Storage,Sessions,Collections,ActionPlans -Label after
```

The runner sets `SAVETOOLKIT_TEST_TEMP`, `TEMP`, `TMP` and `TMPDIR` beneath
`D:/Temp/saveeditor/test-scratch/rev-mutation-core`. It runs one `--mutate` area at a time,
saves a compact JSON summary and log there, and deletes the large raw report before
starting another area. Ordinary builds/tests must run after mutation testing to restore
uninstrumented assemblies. Do not run mutation sweeps and ordinary builds concurrently.

`-Label before` excludes classes named `*MutationEffectiveness*`, allowing the original
test suite to be measured even after the focused regression tests have been added.
The class filters in the runner bound the test selection; allocation benchmarks,
fuzz-worker tests and conformance/documentation checks are outside these sweeps.
Editing and Sessions additionally omit API-host tests to keep per-test capture bounded.
They still run in the full release validation.

## Runner compatibility

This checkout uses xUnit v3 4.0.2, MTP 2.5.1 and the .NET 10 SDK selected by
`global.json`. Stryker.NET **5.0.0** works with its native, preview `mtp` runner.
`tests/stryker-config.json` selects that runner explicitly. The test project links an
optional `SAVETOOLKIT_MUTATION_CONFIG` file as its assembly's `.testconfig.json` to
apply MTP class filters without changing normal test discovery. It also references
the SDK directly so Stryker can select it as the mutation target.

Do **not** substitute the VSTest adapter: an experimental adapter run reported that
every mutant survived, including removal of ZIP payload writes. That result was
discarded. xUnit v3's separate test process needs the native runner to activate
mutants correctly; see [Stryker issue 3117](https://github.com/stryker-mutator/stryker-net/issues/3117).
Stryker's `test-case-filter` requires an MTP `--filter` option unavailable in this
xUnit runner; use the linked MTP configuration instead. See the
[Stryker configuration reference](https://stryker-mutator.io/docs/stryker-net/configuration/)
and [MTP configuration reference](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-config).

The mutation score is `(Killed + Timeout) / (Killed + Timeout + Survived + NoCoverage)`.
Compile-error and ignored mutants are excluded. Stryker's safe-mode compilation
fallback excludes whole methods when generated mutations break definite assignment;
in particular, it cannot measure `WorkspaceExporter.MirrorBackups` in this run.
Scores must therefore be read together with those exclusions, not as proof of
complete path coverage.

## Results and follow-up (2026-10-29)

Measured against source commit `34e7b27b`, using the same source and test selections
before/after. New tests are excluded from `before` by class name, rather than by
reverting source or using a second checkout. The full Release suite also exercises
the tests omitted from the mutation selections.

| Area | Before | After | Detected / valid after |
| --- | ---: | ---: | ---: |
| Core Editing | 65.68% | 80.81% | 219 / 271 |
| Core Export | 77.50% | 87.50% | 35 / 40 |
| Core Storage | 80.28% | 88.73% | 189 / 213 |
| Core Sessions | 65.90% | 86.65% | 902 / 1,041 |
| SDK Collections | 79.55% | 93.18% | 123 / 132 |
| SDK ActionPlans | 55.00% | 100.00% | 20 / 20 |

The additions contain **138 test methods / 219 discovered cases**: 218 pass and one
confirmed bug is skipped. They cover coercion and metadata retirement; import rollback,
paths, twin selection and system-file lookup; conflicting export outputs; query bounds,
reference parameters and retired identities; scalar/structural undo, compensation and
history budgets; request/metadata preview budgets; group candidate isolation, publication,
ownership and reference pinning; inspection integrity; and semantic traversal/memory bounds.

The final remembered-lazy-ID and group-confirmation cases were measured in a follow-up
shard for `DocumentSession.Structure.cs`, `DocumentGroupRegistry.cs` and `Workspace.cs`
(including the session-reuse assertion). The Sessions
aggregate replaces those files' earlier results; other files retain their full-area results.

Confirmed bug: `ImportStorageMutationEffectivenessTests.BackupTwinUsesTheSdimgPrimaryWhenALooseDirectoryHasTheSameName`.
With `PPSA00001/SAVE`, `PPSA00001/sdimg_SAVE` and
`PPSA00001/sdimg_sce_bu_SAVE`, `Twin(backup)` selects the loose `SAVE` directory.
The regression was run and failed before being marked
`[Fact(Skip = "#40 bug: backup twin lookup selects loose SAVE before sdimg_SAVE when both share a name.")]`.
Production code is unchanged.

Full-suite validation also exposed an incorrect assertion in the existing API export
streaming test: loose uploads use their original filenames in the ZIP, rather than
their internal import paths. The test now checks those filenames explicitly and
still verifies original/override bytes, asynchronous transfer and scratch cleanup.

An intermediate full run also hit the existing Tales of Arise
`CipherSetupAllocationsDoNotGrowWithTheNumberOfFeedbackChunks` allocation guard
(5,200 bytes versus 952, exceeding its 2 KiB tolerance). Its subsequent isolated
383-case project run passed unchanged. No plugin code, test or threshold was edited;
the failed-run log is retained alongside the final validation results.

The native MTP runner remains preview software. A separate semantic-review sweep with
coverage analysis **off** still reported selected local-function mutants as surviving
despite focused boundary tests. Their root cause is unresolved; do not reinterpret them
as killed or equivalent. Compiler fallback also excludes 50 Editing, 27 Export,
28 Storage, 455 Sessions, 29 Collections and 5 ActionPlans mutants from the denominators.

Notable residuals (mutant IDs are from this source revision):

| Area | Survivors and reason |
| --- | --- |
| Editing | `41`, `174`, `207`, `286`, `346`: redundant descriptor-count checks, equivalent Int128 boundary/wrapping, unused parse-failure output, an empty-loop break, and agreeing metadata/rendered specs. Other survivors only change diagnostics. |
| Export | `441`, `482`: an empty override set and the disjoint game/system-file lists make these changes equivalent. Three remaining mutations only change diagnostics. |
| Collections | `414` allocates a new empty array instead of the shared empty array; the other eight survivors only change diagnostics. ActionPlans has no valid survivors. |
| Storage | `3378`, `3414`, `3421`, `3422`, `3514`: outer disposal, equivalent path fallbacks, or ZipArchive's own decompression checks preserve behavior. `3448`, `3498`, `3501` affect pooling/CRC allocation only. `3548`–`3550`, `3565`, `3566`, `3569` change ordering/opaque IDs without changing paths, generations, hashes or bytes. Other survivors only change diagnostics. |
| Sessions: traversal | `3026`, `3029`–`3032`, `3035`, `3038`, `3040`, `3044`, `3046`, `3084`, `3095`, `3098`, `3127`: overlapping traversal guards or unresolved native-runner/instrumentation results. Depth, node, tab/row and byte-limit tests exist; a separate coverage-off sweep still reported selected mutants as surviving. Kept visible for follow-up, not discounted from scores. |
| Sessions: integrity/lifetime | `2556` needs a file replacement specifically between fingerprinting and capture; deterministic coverage needs an injectable storage boundary. `3259` changes an exact clock-tick idle cutoff; a deterministic test needs a clock hook. No production hook was added. Neighboring fingerprint and leased-workspace checks are tested. |
| Sessions: equivalent/diagnostic | Remaining null/default, empty-group, exhausted-redo, repeated-disposal, duplicate fingerprint/size checks and checked-offset changes are guarded by surrounding invariants. Cleanup aggregation and field IDs/messages affect diagnostics. Workspace ordering is presentation only. |

Final `./scripts/test.ps1 -Configuration Release` validation passed all 21 projects:
6,997 passed, five skipped (four synthetic-sample cases and the known bug), zero
failed and zero compiler warnings. `SAVETOOLKIT_ARTIFACTS` pointed directly at
`C:\Users\Jonas\repos\private\JKamsker\PS5SaveGame-Toolkit\artifacts`; samples were
not copied or junctioned. Scratch and results remained under the D: task directory.

Documentation checks against this branch's `HEAD` pass all 27 checks. During
the run local `main` advanced beyond the measured source. Comparing this checkout's
documentation to that newer source (`--code-ref main`) fails on the newly added
`/reference-fields/records` route and `AllocationProbe`. Updating those unrelated
documentation files is outside this task's allowed scope.

Recommend an **informational nightly** job with separate area jobs and short report
retention, followed by ordinary tests to restore uninstrumented outputs. Keep it out
of the PR blocking path until native-MTP results and compile-error exclusions are stable.
The committed runner retains compact summaries and deletes large reports between areas.
