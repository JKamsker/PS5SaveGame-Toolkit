# SDK and framework request triage

Tracking: [#40](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40). Initial triage on
2026-10-29 against local `main` at `72cd9973793c0d39556bbf3c233bb6374b80e8bb`.
This is the local integration branch, not the older published `origin/main`.
Evidence paths and line numbers below refer to that initial snapshot. The original review covers
`AGENTS.md`, `PLUGIN_GUIDE.md`, all 17 plugin README SDK-request/adoption sections,
and the 292 comments available from `gh issue view 40 --comments` at collection time
(through comment `6488004888`). No sample bytes or credentials were inspected.

Capabilities and adoption rechecked after the published main integration on 2026-10-29 against
`origin/main` at `201dd690a48a93bf496f1825246bc6954cc5ec52`. This recheck does not claim a new
complete issue-comment census. It includes merged UnrealGvas adoption (PR66), system-output
ownership foundation (PR56), C# size guard (PR59) and idle-race test fix (PR70).
SDK-01's F8 review and web navigation/revert are delivered; SDK-02–05 adoption is recorded in
[the per-plugin status](PLUGIN_GUIDE.md#sdk-0105-adoption-on-main). Selection-table binding and
its opt-in advisory fixture are delivered as SDK-41; their authoritative
[contract and adoption recipe](PLUGIN_GUIDE.md#selection-table-binding-and-advisory-conformance-40)
remain separate from plugin migration. Shared testing helpers are documented in the plugin guide:
[mutation fuzzing](PLUGIN_GUIDE.md#deterministic-mutation-fuzzing-40) and
[allocation probes](PLUGIN_GUIDE.md#allocation-tests-allocationprobe).

Documentation reconciliation against `201dd690a48a93bf496f1825246bc6954cc5ec52` confirms the
delivered baseline and keeps three ledger requests below open. SDK-06 has a shipped lossless JSON
foundation and characterization tests, but remains open until both GenericJson and XorJson
consumers are on `main`. SDK-08 is resolved for the production UnrealGvas consumer: eligible
non-generic Jedi/Sackboy scalar projections use canonical references, with unavailable targets,
canonical metadata and bounded wide-string fallback covered by consumer tests. SDK-10 has the pure
ParamSfo substrate and merged ownership foundation, but remains open until controlled whole-byte
caption evidence and a later SuckerPunch consumer exist. SDK-07 still has no host preset store;
its gate is host storage and compatibility resolution plus the requested FinalFantasy7 and transfer
use. The remaining functionality/UX wave (including PR57/58/60/61/62/63/64/65/67/68/71) remains
unmerged and is not counted as delivery.

**41 deduplicated items: 3 open, 0 partially resolved, 35 resolved, 3 obsolete.**
Counts describe capabilities, not individual comments or plugin migrations. Related
requests are merged; delivered foundations and genuinely different remaining contracts
have separate IDs. “Requested by” also names the plugin candidates explicitly identified
by the cross-plugin report; it does not imply that every listed plugin has migrated.
Routine review fixes that were never requests or deferred framework findings are not
recounted as backlog items.

Rows are ordered by recommended next work, then documentation-only dispositions, then
delivered capabilities. Value is an estimate: confirmed requesters/candidates are counted
separately from possible wider users. Effort: S = a focused change, M = several components
and characterization tests, L = a substantial lifecycle/serialization integration.
“Additive” preserves existing callers; it can still carry substantial behavioral risk.

## Prioritized inventory

| ID | Request | Requested by | Status | Evidence (file:line or commit) | Value | Effort | Risk | Recommendation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SDK-01 | Bounded F8 traversal in original/current semantic review, with labels, navigation and individual revert | TalesOfArise | resolved | `src/SaveToolkit.Core/Sessions/SemanticQueryableTraversal.cs`; `SemanticReview.cs`; `SemanticChangeLog.cs`; `tests/SaveToolkit.Core.Tests/QueryableSemantic*Tests.cs`; `web/src/components/document/review/ChangesPanel.tsx`; `web/src/components/document/review/useSemanticJump.ts`; [API contract](API.md#preview-and-semantic-review) | At least 3 F8 editors benefit: TalesOfArise, Pragmata and FinalFantasy16; users can review/revert quantities without opening every page | M | Additive Core behavior; original identity and budget correctness | Backend now scans separate original/current documents with global row/page/byte caps, includes empty slots, names changes, registers individual revert and returns optional `{collectionId,rowKey,query}` navigation targets. Cuts/invalid pages explicitly report truncated incomplete coverage. Web UI displays collection/row/field labels, original/current values and incomplete/truncated warnings even for zero results. Jump loads the exact encoded target query at the review revision, opens enclosing sections/tabs and focuses the row field without editing. Individual Revert honors `canRevert`; mutations and stale targets refresh review/pages. SDK-02 metadata and alias guards remain unchanged. |
| SDK-02 | Publish field metadata and canonical primary/derived binding policy independently of rendered views | Pragmata | resolved | `plugins/Pragmata/README.md:478`; [design](plans/F13-FIELD-SPEC-PUBLISHING.md); storage foundation `a1c08c0`; implementation `de78531`; `src/SaveToolkit.Sdk/FieldMetadata.cs`; `src/SaveToolkit.Core/Editing/FieldMetadataIndex.cs` | 1 explicit requester; at least 3 candidate editors with unloaded fields (Pragmata, FinalFantasy16, TalesOfArise) | — | Delivered additive opt-in capability | Implement `IFieldMetadataDocument`; resolve requested IDs in batches of at most 200 with live bindings, canonical specs and primary/read-only-derived policy. Core validates unloaded edits and F6 plans, compares metadata again at apply, and resolves primaries for undo/redo after structural rebuilds. Pragmata has adopted this contract: its resolver registers selected canonical quantity setters without views and publishes legacy aliases as read-only derived bindings; its existing binding guard is retained. Targeted semantic-review coverage and automatic group forwarding are separate integrations. |
| SDK-03 | Share pure embedded reference-pack decoding for direct `Open(file)` fallback | FinalFantasy7, FinalFantasy16, GodOfWarRagnarok | resolved | `1a348ab`; `src/SaveToolkit.Sdk/References/EmbeddedReferenceContext.cs`; `tests/SaveToolkit.Core.Tests/References/EmbeddedReferenceEquivalenceTests.cs`; original [duplication report, item 6](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6486086567) | 3 plugins; direct and hosted opens can use the same names, icons and attribution without duplicated parsing | — | Delivered additive pure factory; per-pack decoded snapshots and SDK/Core equivalence; ambiguous resources/JSON are rejected | Use `EmbeddedReferenceContext.Load(assembly, pluginId, titleId, limits)` or cache `Load(assembly)` and `Pin` per open. Core shares the decoder while retaining ZIP persistence, caches and usage tracking. FinalFantasy7, FinalFantasy16 and GodOfWarRagnarok have adopted the shared decoder, retaining host overlays, game-specific joins and missing-image/provenance policy. See [embedded fallback](PLUGIN_GUIDE.md#embedded-fallback-for-direct-opens) and [per-pack snapshot maintenance](PLUGIN_GUIDE.md#embedded-pack-snapshot-baseline). |
| SDK-04 | Compose group member identity mapping and dispatch | Gearbox, XorJson | resolved | [duplication report, item 4](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6486086567); `f869910`; `src/SaveToolkit.Sdk/DocumentGroups/DocumentMemberAdapter.cs`; characterization `c6fdcf1` | 2 plugins; shared field/action/collection/navigation mapping with explicit compatibility policies | — | Delivered additive composition helper; existing group APIs unchanged; Gearbox and XorJson adopted | Use `DocumentMemberAdapter(role, document, identityPolicy)` and `DocumentMemberRouter`. Preserve each plugin's query/eager-row, navigation, graph/marker, collection-column and F6 scope policies; keep transfer/dependency/snapshot/rollback policy local. Exact adoption recipes are in `docs/PLUGIN_GUIDE.md` and the `GroupAdapterPolicies` compatibility fixture. |
| SDK-05 | Preserve exact icon alt text and historical missing-byte/provenance behavior in shared icon helper | BaldursGate3, TalesOfArise | resolved | `0e31467`; `src/SaveToolkit.Sdk/References/ReferenceIconOptions.cs`; `tests/SaveToolkit.Core.Tests/SdkHelpers/ReferenceIconCompatibilityTests.cs`; original requests: `plugins/BaldursGate3/README.md:533`, `plugins/TalesOfArise/README.md:567` | 2 plugins; consistent migration without losing publisher notices or changing an image node to text | — | Delivered additive overload/options; original signature and output retained | Use the five-argument `ReferenceViews.Icon` overload with `ReferenceIconOptions`: BG3 selects `RequireProvenance`; TalesOfArise selects `AssetIdOnly`, exact role/publisher alt text and the em-dash fallback. Sources registration is retained. BaldursGate3 and TalesOfArise have adopted these options, retaining exact alt text, missing-byte behavior and Sources registration; compatibility tests pin their output. See the shared-helpers migration examples in `docs/PLUGIN_GUIDE.md`. |
| SDK-06 | Shared lossless JSON lexical tree and byte-preserving scalar patch substrate | GenericJson, XorJson | open | Foundation merged in `a3964566`; `src/SaveToolkit.Sdk/Json/LosslessJsonDocument.cs`; `tests/SaveToolkit.Core.Tests/LosslessJsonDocumentTests.cs`; [tutorial](../docs-site/tutorials/lossless-json.md) | 2 plugins; fewer preservation discrepancies across ordinary and transformed JSON | L | Additive library; adoption still has serialization and stable-identity risk | Foundation is shipped with strict UTF-8/BOM/trivia/duplicate/exact-number/limits/snapshot coverage. Close only after both GenericJson and XorJson consumers land on `main`; keep structural IDs and XOR envelopes local. |
| SDK-07 | Versioned toolkit preset storage outside save bytes, with title/build and owned-instance resolution | FinalFantasy7 (R19) | open | `plugins/FinalFantasy7/README.md:540`; `src/SaveToolkit.Sdk/ActionPlan.cs:7` describes action plans; no persisted preset capability or host preset store on `main` | 1 confirmed requester; reusable loadouts/settings could benefit more plugins, but safe native loadout writing is not yet established | L | Additive host storage plus a new versioned compatibility/resolution contract | Design host-owned storage and explicit missing/ambiguous-instance resolution. Keep FinalFantasy7 and transfer adoption gates open; initially document pure F6 parameter plans as the workaround. Never put persistence I/O in a plugin or imply safe native ownership. |
| SDK-08 | Canonical field-reference presentation node | UnrealGvas | resolved | `201dd690`; `src/SaveToolkit.Sdk/Views/FieldReferenceNode.cs`; `src/SaveToolkit.Sdk/FieldPresentation.cs`; `plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas/GvasFieldReferenceFactory.cs`; `plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas.Tests/GvasFieldReferenceTests.cs`; [tutorial](../docs-site/tutorials/field-references.md) | 1 confirmed requester; avoid repeated binding/spec publication when a saved value appears in several projections | M | Additive wire node; consumer-specific evidence remains title-bound | Production UnrealGvas adoption covers eligible non-generic Jedi/Sackboy scalar projections across raw, named and eager Search views. Tests cover canonical metadata, shared live edits, unavailable structural targets, protected fields and bounded wide-string fallback; generic summaries, actor observations and opaque values retain safe fallbacks. Other titles require separate evidence. |
| SDK-09 | Typed web client method for catalog record-detail endpoint | Framework/API contract review; no plugin-specific requester | resolved | `261af65`; [deferred API finding](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6485719444); `web/src/api/client.ts` adds `api.referenceRecord`; `web/src/api/client.references.test.ts` covers the documented detail envelope, encoded IDs/key, 404 ProblemDetails and cancellation | 0 direct plugin changes; all reference consumers can reuse a correctly typed attributed detail response | — | Delivered additive client method; no wire change | Use `api.referenceRecord(...): Promise<ReferenceRecordDetail>` with an optional `AbortSignal` and normalized request/error handling. No existing UI calls use this catalog endpoint; reference-field candidate lookups remain unchanged. |
| SDK-10 | Define whether gameplay timer/difficulty edits should update `param.sfo SUBTITLE` | SuckerPunch | open | Foundation `a3964566`; ownership foundation `201dd690`; `src/SaveToolkit.Sdk/Saves/ParamSfoDocument.cs`; `src/SaveToolkit.Sdk/Saves/ParamSfoPatchPlan.cs`; `docs/SYSTEM_OUTPUT_OWNERSHIP.md`; `tests/SaveToolkit.Core.Tests/ParamSfoPatchTests.cs`; [tutorial](../docs-site/tutorials/sfo-substrate.md) | 1 plugin; save-manager captions might reflect edits, but the dependency is unproven | M | Contract change if automatic; metadata preservation risk | The substrate remains identity-bound pure patching with strict UTF-8/capacity/NUL checks and byte preservation. The separate ownership foundation is shipped, but close only after controlled whole-byte caption evidence and a later SuckerPunch consumer; do not claim automatic synchronization today. |
| SDK-11 | Reject/coalesce duplicate scalar IDs because undo allegedly replays old values forward | Witcher3 | obsolete | `plugins/Witcher3/README.md:530`; `src/SaveToolkit.Core/Sessions/DocumentSession.cs:231` uses `useOld ? batch.Reverse() : batch`, present since `e08c5b0`; [SDK rollback report](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6485651871) explicitly preserves ordered duplicates | 1 stale defect report; no established shared undo failure on this baseline | S (docs) | A global rejection would change the existing ordered-transition contract | Correct the backlog claim; retain the plugin's stricter local guard if desired. Do not introduce rejection/coalescing solely on this reproduction. Reverse transitions `0→1→2` restore `2→1→0`; a different failure needs its own reproduction. |
| SDK-12 | Treat full-payload shared CRC latency as a framework defect | FinalFantasy16 performance review | obsolete | [deferred opportunities](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6487461287); [framework disposition](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6487860350); `src/SaveToolkit.Sdk/Binary/Crc32Ieee.cs:12` computes the required coverage | 1 measurement; no validated avoidable work or correctness defect | S (docs) | Replacing/skipping coverage risks integrity | Won't remove mandatory checksum/reparse work. Record the measurement as a profiling observation; only optimize an equivalent algorithm after independent vectors and actual comparative evidence. |
| SDK-13 | Longer F2 licence field for mixed-source notices | AtomicHeart | obsolete | `plugins/AtomicHeart/README.md` SDK requests records the accepted shorthand/sidecar workaround; `src/SaveToolkit.Core/References/PackManifestValidator.cs:121` caps licence text at 100 characters | 1 plugin; full rights notices are already preserved in attribution/sidecars | S (docs) | Increasing limits is additive but unnecessary for the reported dataset | Document concise licence identifiers plus complete author/modification metadata and source sidecars. No framework change requested or needed for this case. |
| SDK-14 | Shared bounded SaveDiff traversal of F8 views beyond the first 200 rows | BaldursGate3, Cyberpunk2077, EldenRing, FinalFantasy7, FinalFantasy16, Gearbox, Insomniac, Pragmata, SuckerPunch, TalesOfArise, UnrealGvas, Witcher3 | resolved | `8063471`; `src/SaveToolkit.Testing/Diff/PluginViewTraversal.cs:9`; `Diff/QueryableViewTraversal.cs:25` | 12 named README consumers; complete bounded evidence with explicit truncation | — | Delivered additive traversal | Use `PluginViewDecoder` and `DecodeLimits`; this is independent of semantic review (SDK-01), whose backend and web navigation/revert UI are also delivered. |
| SDK-15 | Production SDK-only native decoder registration in stock SaveDiff CLI | AtomicHeart, Cyberpunk2077, EldenRing, FinalFantasy7, FinalFantasy16, Gearbox, Insomniac, Pragmata, SuckerPunch, TalesOfArise, UnrealGvas, Witcher3 | resolved | `8063471`; `src/SaveToolkit.Sdk/Diff/ISaveDecoderProvider.cs:6` | 12 named SDK sections; native evidence without a Testing dependency or custom CLI | — | Delivered optional capability | Implement `ISaveDecoderProvider.Decoders` / `ISaveDecoderHook`; select hooks explicitly. No hook is required when the shared view decoder suffices. |
| SDK-16 | Non-mutating inspection on read-only documents | Cyberpunk2077, EldenRing (R22), FinalFantasy16, Gearbox, Insomniac, TalesOfArise, UnrealGvas, Witcher3 | resolved | `8b83aaa`; `src/SaveToolkit.Sdk/Views/ViewNode.cs` `ActionNode.Mutates`; `src/SaveToolkit.Core/Sessions/InspectionActionRunner.cs:11` | 8 README consumers; attributed inspection remains available when save writes are disabled | — | Delivered additive opt-in | Publish `Mutates = false`; respect isolated-copy and message-only outcome rules. |
| SDK-17 | Browse-only reference pickers and native catalog category filtering/sorting | BaldursGate3, Cyberpunk2077, EldenRing, FinalFantasy16, Gearbox, Insomniac, TalesOfArise, UnrealGvas, Witcher3 | resolved | `8b83aaa`; `src/SaveToolkit.Sdk/References/IReferenceCatalog.cs`; `src/SaveToolkit.Core/References/LoadedReferencePack.cs` | 9 named SDK sections; users can inspect IDs/provenance without enabling edits or building local picker indexes | — | Delivered additive query/picker support | Use `ReferenceSpec`, `ReferenceQuery.Category` / `SortBy` and declared `SortKeys`; keep genuinely richer plugin-specific F8 filters. |
| SDK-18 | Float duration formatting | SuckerPunch | resolved | `8b83aaa`; `src/SaveToolkit.Sdk/Values/FieldSpec.cs` `FloatFormat.Duration` / `FloatSpec.UnitsPerSecond`; `plugins/SuckerPunch/README.md:595` | 1 explicit request; evidenced Tsushima float timer has readable duration | — | Delivered additive hint | Set units only with format evidence; retain raw float precision. |
| SDK-19 | F4 reference parameters inside F6 planned-action dialogs | AstroPlayroom, GenericJson | resolved | `63580dc`; `web/src/components/document/review/PlannedActionButton.tsx` passes `valueId` and `actionId`; Core `DocumentSession.cs:197` resolves action parameter specs | 2 requesters; searchable attributed action choices work | — | Delivered client binding fix | Adopt `ReferenceSpec`; GenericJson's remaining enum fallback is plugin adoption, not a missing API. |
| SDK-20 | Exact-number/union values render meaningfully in previews and Changes | GenericJson | resolved | `03b66b7`; `web/src/api/actionReview.ts:38` `reviewValue` / `formatValue`; SDK `Values/TaggedFieldValueJson.cs` | 1 requester; exact preview values replace object-string output | — | Delivered renderer fix | Use tagged F10 values and shared review rendering. |
| SDK-21 | Opaque collection IDs containing `/` survive HTTP querying | GenericJson; UnrealGvas affected by the same route | resolved | `c1762df`; `src/SaveToolkit.Api/Endpoints/WorkspaceEndpoints.cs:76`; `web/src/api/client.ts` `queryCollection`; `tests/SaveToolkit.Core.Tests/OpaqueCollectionApiTests.cs` | 2 documented cases; every collection can keep its native opaque ID | — | Delivered additive route | Use `GET .../collections?collectionId=...`; keep old path route for compatibility. Do not indiscriminately decode the old route twice. URL-safe plugin IDs remain valid. |
| SDK-22 | Explicit, atomic, fingerprinted multi-file ownership, staging, history and export | Gearbox (R4), XorJson | resolved | `6ae1c6c` (merged `7ff21ad`); `src/SaveToolkit.Sdk/DocumentGroups.cs:6`; `src/SaveToolkit.Core/Sessions/Groups/StagedGroupDocument.cs` | 2 adopted plugins; linked character/profile outputs commit and undo together | — | Delivered optional group capability | Use `IDocumentGroupPlugin` and `IGroupedSaveDocument.CaptureMemento`; no alternative output/transaction API is needed. SDK-04 only shares member adapters. |
| SDK-23 | Overlay preserves untouched sibling bytes instead of exposing empty files | XorJson | resolved | `4532481`; `src/SaveToolkit.Core/Storage/OverlaySaveSet.cs`; `plugins/XorJson/README.md:329` | 1 reported case; safe verification of partial output sets | — | Delivered Core fix | Return changed outputs only; retain preservation/unchanged-file evidence. |
| SDK-24 | Common F8 in-memory query pipeline and table shell | AstroPlayroom, AtomicHeart, Cyberpunk2077, EldenRing, FinalFantasy7, FinalFantasy16, Gearbox, Insomniac, Pragmata, TalesOfArise, UnrealGvas | resolved | [duplication item 1](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6486086567); `1bbf6a2`; `src/SaveToolkit.Sdk/Collections/InMemoryCollection.cs:15` | 11 named candidates; consistent full-model filtering/sorting with bounded rendering | — | Delivered additive helper | Use `InMemoryCollection<T>.Query` and `QueryableCollection.ToTableNode()` where policy adapters preserve behavior; keep database/partial-model engines. |
| SDK-25 | Common F6 plan equality/revalidation and scalar application | AstroPlayroom, AtomicHeart, Cyberpunk2077, EldenRing, FinalFantasy7, FinalFantasy16, GodOfWarRagnarok, Insomniac, SuckerPunch, TalesOfArise | resolved | [duplication item 2](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6486086567); `1803b2d`; `src/SaveToolkit.Sdk/ActionPlans.cs:25` | 10 candidates; fewer stale/tampered-plan and warning-policy differences | — | Delivered additive helper | Use `ContentEquals`, `Revalidate`, `ApplyScalar`; explicitly select actual derived writes and warning merge policy. Core still owns tokens/revisions/query scope. |
| SDK-26 | Bounded binary ranges/cursors and strict FString readers/writer | AtomicHeart, BaldursGate3, Cyberpunk2077, EldenRing, Gearbox, UnrealGvas, Witcher3 | resolved | [duplication item 3](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6486086567); `9e12bfa`; `src/SaveToolkit.Sdk/Binary/BoundedBinaryCursor.cs:12`; `Binary/FStringCodec.cs:26` | 7 candidates; consistent malformed-input bounds while retaining format policy | — | Delivered additive helpers | Use `BinaryRange`, `BoundedBinaryCursor`, `FStringCodec` / `FStringLimits`; preserve diagnostic adapters, byte/unit limits and original encoding bytes. |
| SDK-27 | Complete bounded reference-catalog traversal | EldenRing, FinalFantasy7, Insomniac, TalesOfArise, UnrealGvas, Witcher3 | resolved | [duplication item 7](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6486086567); `b5397b4`; `src/SaveToolkit.Sdk/References/ReferenceCatalogReader.cs:11` | 6 candidates; malformed/incomplete catalogs cannot silently become complete indexes | — | Delivered additive helper | Use `ReadBounded` with an explicit budget; keep direct `Find`/`Search` for ordinary lookup and cache complete indexes once. |
| SDK-28 | Shared attributed icon and read-only reference-inspection mechanics | AtomicHeart, BaldursGate3, Cyberpunk2077, FinalFantasy16, Gearbox, Insomniac, TalesOfArise, Witcher3 | resolved | [duplication item 8](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6486086567); `b5397b4`; `src/SaveToolkit.Sdk/References/ReferenceViews.cs:12`; `References/ReferenceInspection.cs:13` | 8 candidates; correct separate record/image attribution and pinned selection | — | Delivered additive helpers | Use `ReferenceViews.Icon`, `ReferenceInspection.Create` / `ResolveSelection`; preserve local compatibility adapters described in SDK-05. |
| SDK-29 | Shared incremental IEEE CRC32 primitive | FinalFantasy16, Gearbox, GodOfWarRagnarok | resolved | [duplication item 9](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6486086567); `e226da4`; `src/SaveToolkit.Sdk/Binary/Crc32Ieee.cs:12` | 3 candidates, plus Core importer adoption; shared independently tested primitive | — | Delivered additive helper | Use `Compute` or `Append` / `GetCurrentHash`; keep coverage/folding/endian policy local. CRC32C is a different algorithm. |
| SDK-30 | Remove per-row Entry objects/clones and excess query materialization | FinalFantasy7; AstroPlayroom follow-up; shared Collections review | resolved | [deferred FF7 finding](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6487271271); `63be7a2`; `src/SaveToolkit.Sdk/Collections/InMemoryCollection.cs:145` uses a value-type Entry | Existing helper consumers; cheaper full-model queries without stale result caching | — | Delivered internal optimization | Keep the existing API. Preserve complete key validation, live projections, empty counts and ascending ordinal ties. Old FF7 allocation figures describe the previous implementation. |
| SDK-31 | Caller-owned Kraken destination and reusable workspace | AtomicHeart; Codecs review (UnrealGvas also benefits) | resolved | [deferred finding](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6487490821); `73e5bf3`; `src/SaveToolkit.Codecs/OodleCodec.cs:31`; adoption `28a10e4` | 2 codec consumers; fewer independent-stream copies/scratch allocations | — | Delivered additive overload/workspace | Use `Decompress(source, destination, OodleWorkspace, maxBytes)`. AtomicHeart adoption is already on this baseline; older deferred-adoption comments are superseded. |
| SDK-32 | Register storage bindings without creating temporary FieldNodes | FinalFantasy16 | resolved | [deferred finding](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6487461287); `a1c08c0`; `src/SaveToolkit.Sdk/BoundSaveDocument.cs:67` | 1 measured requester; other eager-binding plugins can reduce opening allocations | — | Delivered additive protected API | Adopt `RegisterField(valueId, spec, get, set, validate)` and later `Field(valueId, label)`. FinalFantasy16 migration remains separate; host metadata discovery is SDK-02. |
| SDK-33 | Avoid repeating pinned catalog selection/usage allocations | FinalFantasy16; shared Core performance review | resolved | [deferred finding](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6487461287); `ab99e28`; `src/SaveToolkit.Core/References/ReferenceContext.cs:36` | All 17 reference-consuming plugins potentially benefit; repeated lookups reuse pinned selections and misses | — | Delivered internal bounded cache | Use normal `Catalog` / `CatalogForHost`; cache saturation falls back to ordinary selection. Do not add plugin-side substitutes. |
| SDK-34 | Remove 81,920-byte scratch allocation per embedded pack file | EldenRing, FinalFantasy16; SuckerPunch cold-load observation | resolved | `286b064`; `src/SaveToolkit.Core/References/PackFileSource.cs:22`; nonseekable coverage in `07417c0` | All embedded-pack consumers; lower cold-load churn | — | Delivered internal reader optimization | Use Core's existing `PackFileReader.ReadBounded`: exact seekable reads and pooled nonseekable scratch. Distinguish later import-buffer reuse (`07417c0`) from this already-delivered fix. |
| SDK-35 | SaveDiff F8 traversal accepts 2+2+2 rows for declared total 5 | SaveDiff tool/test review; no plugin implicated | resolved | [deferred finding](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6487244022); `d87349c`; `src/SaveToolkit.Testing/Diff/QueryableViewTraversal.cs:54` checks remaining count | Every F8 view-decoder consumer; surplus rows cannot be reported as complete evidence | — | Delivered shared-engine fix | Retain remaining-cardinality validation and stop exactly at the declared total. Global caps still mark truncated evidence. |
| SDK-36 | All-plugin SaveDiff discovery fails on duplicate `ffxvi` hook ID | SuckerPunch | resolved | `383a942`; `plugins/FinalFantasy16/SaveToolkit.Plugins.FinalFantasy16/FaithEvidenceDecoder.cs:13` is `ffxvi-evidence`; `src/SaveToolkit.Sdk/Diff/ISaveDecoderProvider.cs:8` requires unique IDs | All 17 bundled plugins can be discovered together | — | Delivered plugin registration correction; no SDK change | Use the ordinary CLI; the isolated-copy workaround is obsolete. Keep collision rejection, not a rule allowing hooks to replace plugin IDs. |
| SDK-37 | Shared SampleSaves ignores `SAVETOOLKIT_TEST_TEMP` and leaks temporary imports | SuckerPunch; framework test-quality review | resolved | `6d5771c`, `d63b930`; `src/SaveToolkit.Testing/SampleSaves.cs:16` uses `TestScratch.ProcessFolder`; `Testing/TestScratch.cs:14` names the override | All 17 plugin test projects; imports honor configured scratch and process cleanup | — | Delivered Testing behavior | Set `SAVETOOLKIT_TEST_TEMP`; retain TEMP/TMP for other tools. No need for a second plugin-local sample importer. |
| SDK-38 | Download original files while a document is read-only | UnrealGvas | resolved | `e08c5b0`; `src/SaveToolkit.Api/Endpoints/WorkspaceEndpoints.cs:93` falls back to `session.File.OpenRead()` without `CanEdit` gating; group-specific exception added in `6ae1c6c` | 1 historical requester; read-only inspection still permits retrieving original bytes | — | Existing API behavior | Use `/download` for independent documents; groups use whole-workspace export. No new capability is needed. |
| SDK-39 | Rich image/nested cells instead of blank table cells | UnrealGvas; Insomniac R5/R8/R9 concern | resolved | `dcd836c`; `src/SaveToolkit.Sdk/Views/ViewNode.cs` permits arbitrary ViewNode cells; `web/src/components/document/table/TableCell.tsx:39` renders images and detail chips | 2 historical concerns; icons and nested content remain reachable | — | Delivered renderer support | Use `ImageNode` and existing rich cells/details. No concrete additional image-cell requirement is specified; SDK-05 concerns helper fidelity, not cell support. |
| SDK-40 | Mixed platform newlines from shared SaveDiff text renderer reach CLI output | SaveDiff tool/test review | resolved | [outside-scope renderer observation](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6487099886); `6c4353d`, `20a84cf`; `tools/SaveToolkit.SaveDiff/CliApp.cs:95` normalizes at emission | Every CLI report consumer; deterministic stdout/file bytes on Windows and Unix | — | Delivered CLI boundary normalization | Keep `CliApp.Emit` normalization. Shared `TextReportWriter` still uses a platform blank line internally; no unresolved CLI contract remains. |

| SDK-41 | Explicit selection-table binding for sibling planned controls and opt-in ownership evidence | FinalFantasy7, TalesOfArise, SuckerPunch; SDK API review | resolved | [API review proposal](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6489901577); `src/SaveToolkit.Sdk/Views/ViewNode.cs` `ActionNode.SelectionTableId` / `TableNode.SelectionId`; `src/SaveToolkit.Testing/SelectionScopeFixture.cs` | Sibling controls can use explicit or full-query selections/exclusions without moving into cells | — | Delivered additive opt-in; no plugin migration | Adopt only with explicit ownership evidence and browser scope coverage. See the authoritative [shipped contract](PLUGIN_GUIDE.md#selection-table-binding-and-advisory-conformance-40). |

### SDK-01 review identities after SDK-02 adoption

SDK-01 retains distinct exposed value IDs as distinct review entries; Core must not infer alias
identity from matching values, transitions or ID prefixes. The earlier Pragmata example of
500 entries for 250 quantity edits is historical. Its current Raw, F8 and evidence quantity
projections reuse canonical IDs. `InventoryCollectionTests.cs` asserts **250 review entries for
250 edits** in a 251-row fixture, individual revert through either projection, stale/double-revert
rejection and exact undo/redo. `UnloadedQuantitySessionTests.cs` separately asserts one review
entry when legacy read-only derived aliases are reported. Both files are under
`plugins/Pragmata/SaveToolkit.Plugins.Pragmata.Tests/`; the identity/alias definitions are in
`plugins/Pragmata/SaveToolkit.Plugins.Pragmata/Editing/PragmataBindings.cs`.

SDK-02 supplies primary/derived replay policy; `FieldMetadata` does not publish a general
cross-ID canonical relationship. Targeted original/current semantic metadata capture, automatic
group forwarding and generic cross-ID review deduplication remain separate integrations, not
pending SDK-02 delivery or a remaining Pragmata quantity-duplication defect. Preserve complete
direct/derived effects and undo semantics, and do not hide distinct unclassified fields.

## Resolved appendix

The 35 resolved rows identify the delivery and the surviving API or
implementation above. All 29 earlier deliveries (SDK-01 adds its web UI in `520ee2f`), including SDK-05 (`0e31467`, merged in
`bfb666d`), were checked as ancestors of the reviewed local `main`; SDK-02, SDK-03 and SDK-04 name their implementation commits `de78531`, `1a348ab` and `f869910`. An issue comment saying “fixed” was not treated as sufficient evidence by itself.

- **Icon compatibility (SDK-05):** `0e31467` preserves the original four-argument overload
  and adds a five-argument overload with `ReferenceIconOptions.ExactAltText` and
  `ReferenceIconAvailability`. Defaults still require both bytes and provenance, so a
  provenance-only image falls back. BG3 can select `RequireProvenance` to retain that image;
  TalesOfArise can select `AssetIdOnly` with its exact role/publisher notice and `TextNode("—")`
  fallback. Composed alt text always requires provenance; exact text is passed through
  without trimming or appending notices. Asset resolution retains Sources registration in
  all policies. The SDK tests copy both small historical adapters and compare complete nodes
  across missing-byte/provenance combinations, missing records/IDs, empty IDs and icon roles.
  Plugin files remain unchanged; migration is a separate opt-in.
  Validation: 29 new compatibility cases and all 714 Core tests pass; the API builds with
  zero warnings. The full sample-backed Debug script completed all 21 projects with
  6,393 passing and two failing allocation checks (AtomicHeart codec workspace and GenericJson
  opening/first view). Release retries also encountered AstroPlayroom opening allocations;
  isolated checks pass, but no green full-suite run is claimed. These plugin paths do not
  call `ReferenceViews`; fixing their performance tests is outside this SDK-only change.
  All test scratch/TEMP and reports use `D:/Temp/saveeditor/test-scratch/sdk05`.

- **Canonical field publication (SDK-02):** `de78531` adds `IFieldMetadataDocument`,
  `FieldMetadata`, `FieldBindingKind`, and metadata-aware `BoundSaveDocument.RegisterField`.
  Core combines registrations with views, rejects conflicting specs, validates unloaded edits
  and planned changes, snapshots reference policies, and re-resolves live primary bindings for
  replay. Non-opt-in documents retain view-only behavior. Canonical labels/paths are published;
  targeted original/current semantic review and group member identity forwarding are not
  claimed by this delivery. No plugin is migrated in this commit.
- **Evidence tooling (SDK-14/15/35/36/40):** F12 `8063471` supplies both bounded
  `PluginViewDecoder` traversal and the optional SDK decoder hooks. Cardinality was
  subsequently tightened by `d87349c`; `383a942` fixes the conflicting plugin hook.
  The stock CLI retains explicit hook selection and collision rejection. CLI newline
  normalization resolves the reported output problem without claiming that every
  shared text-rendering API changed.
- **Inspection and presentation (SDK-16–21/39):** F13 `8b83aaa` supplies isolated
  `Mutates=false` inspection, browse-only pickers, category/sort queries and float
  duration hints. F3 `dcd836c`, planned-reference binding `63580dc`, tagged review
  rendering `03b66b7`, and opaque query route `c1762df` are separate delivered fixes.
  The path-form collection route was retained, not retroactively made slash-safe;
  the query-string route is the supported opaque-ID path.
- **Transactions and preservation (SDK-22/23/38):** F7 `6ae1c6c` supplies explicit
  ownership, immutable input validation, staged all-member publication, whole-group
  mementos and verified export. `4532481` fixes untouched overlay reads. Original
  independent-file downloads already existed in `e08c5b0`; grouped downloads must
  still use export. Format-specific relationships and console acceptance remain
  plugin/owner evidence, not absent framework APIs.
- **Member composition (SDK-04):** `f869910` adds the non-owning adapter, explicit
  identity/row/navigation/scope policy and preflight-only router. Tests first captured
  the unchanged public Gearbox/XorJson behavior (`c6fdcf1`), then compared SDK mappings
  to both copies and exercised Core staged group rollback, query-scoped plans and
  undo/redo. Member `Revert` tokens remain local; groups replace them with whole-group
  snapshots and recreate adapters/routers if restore replaces models. Plugin adoption
  is deliberately separate from SDK delivery.
- **Six delivered extraction recommendations (SDK-24–29):** the report's items
  1/2/3/7/8/9 map respectively to `1bbf6a2`, `1803b2d`, `9e12bfa`, `b5397b4`,
  `b5397b4`, and `e226da4` (merged in `8ff24d7`). Item 4 is now SDK-04 (`f869910`);
  item 5 remains open as SDK-06; item 6 is delivered as SDK-03 (see the
  [embedded fallback contract](PLUGIN_GUIDE.md#embedded-fallback-for-direct-opens)).
  Existing adapters may retain exact diagnostics, encoding limits, warning order,
  reference provenance and unusual historical availability behavior. The two icon
  compatibility requests are merged into SDK-05 and now have explicit compatibility options.
- **Deferred performance findings (SDK-30–34):** `63be7a2` reduces collection query
  intermediates without caching mutable results. `73e5bf3` adds the bounded reusable
  codec path; AtomicHeart adopts it in `28a10e4` on the reviewed baseline.
  `a1c08c0` adds storage registration; `ab99e28` caches pinned catalog selections;
  `286b064` had already removed per-pack-file scratch churn. `07417c0` adds import
  copy-buffer reuse and nonseekable pack-reader characterization. These deliveries
  did not implement SDK-02's host metadata/alias lifecycle; `de78531` now supplies
  the opt-in metadata, preflight and replay integration.
- **Scratch ownership (SDK-37):** `6d5771c` redirects SampleSaves through the
  configured, process-cleaned `TestScratch`; `d63b930` extends that ownership to
  framework and SaveDiff test imports. Old SuckerPunch workaround text is historical.

## Coverage and dispositions

All SDK-request sections were read: [AstroPlayroom](../plugins/AstroPlayroom/README.md#sdk-requests),
[AtomicHeart](../plugins/AtomicHeart/README.md#sdk-requests),
[BaldursGate3](../plugins/BaldursGate3/README.md#sdk-requests),
[Cyberpunk2077](../plugins/Cyberpunk2077/README.md#sdk-requests),
[EldenRing](../plugins/EldenRing/README.md#sdk-requests),
[FinalFantasy7](../plugins/FinalFantasy7/README.md#sdk-requests),
[FinalFantasy16](../plugins/FinalFantasy16/README.md#sdk-requests),
[Gearbox](../plugins/Gearbox/README.md#sdk-requests),
[GenericJson](../plugins/GenericJson/README.md#sdk-requests),
[GodOfWarRagnarok](../plugins/GodOfWarRagnarok/README.md#sdk-requests),
[Insomniac](../plugins/Insomniac/README.md#sdk-requests),
[Pragmata](../plugins/Pragmata/README.md#sdk-requests),
[SuckerPunch](../plugins/SuckerPunch/README.md#sdk-requests),
[TalesOfArise](../plugins/TalesOfArise/README.md#sdk-requests),
[UnrealGvas](../plugins/UnrealGvas/README.md#sdk-requests),
[Witcher3](../plugins/Witcher3/README.md#sdk-requests), and
[XorJson](../plugins/XorJson/README.md#sdk-requests).

“None blocking” does not erase the optional requests or historical resolutions recorded
in the same section. Conversely, adopting F1–F14 does not create fourteen new requests.
GodOfWarRagnarok reports no missing SDK contract; its shared CRC/reference candidates
are included through the duplication report. GenericJson's enum migration and
FinalFantasy16's storage-registration adoption remain plugin work after API delivery.

The #40 cross-plugin report's nine deferred recommendations are all accounted for.
The explicitly deferred typed-client, F8-cardinality, Collections, codec, field-binding,
catalog-selection and pack-reader findings are represented above, including later fixes.
The DiskSaveFile-based test setup noted in comment `6487174348` was explicitly **not**
a confirmed leak or requested contract replacement: existing `TestScratch` ownership is
the documented workaround, not a new memory-backed DocumentSession backlog item.

Deferred offline importer refactors (EldenRing, TalesOfArise, FinalFantasy16), frozen
plugin attribution strings (Insomniac, GodOfWarRagnarok), and disposal of reviewers'
scratch directories are outside the SDK/framework backlog. They remain plugin/tool or
orchestrator follow-ups on #40. Missing controlled captures, safe structural writers,
socket/owner joins, coupled-file evidence, map alignment and console validation are
likewise not missing framework capabilities. No new framework API can establish them.

Original triage validation: `python docs/tests/test_documentation.py`.
The original triage assessed runtime behavior by source/history inspection and changed
only this document. SDK-05's subsequent implementation and sample-backed validation
are recorded separately in its resolved appendix entry above. The framework documentation pass
also updates linked contracts and dependency-free drift checks.

The initial triage was validated with `python docs/tests/test_documentation.py` and
source/history inspection only. SDK-01's later backend implementation and synthetic
regressions are recorded in its row; its web UI follow-up is delivered in `520ee2f`.
The SDK-04 follow-up adds executable plugin characterization and SDK/Core composition tests,
with samples and all test scratch on D: as described in the implementation report on #40.
