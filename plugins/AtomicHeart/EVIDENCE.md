# Atomic Heart evidence, 2026-10-28

Current branch: `plugin/atomicheart-writes` (preceded by `plugin/atomicheart-roadmap`). Scope: PPSA10695 only, observed engine 4.27.2,
package 522. Patch/region/DLC and known in-game counts are unknown.

## Corpus and F1

`PPSA10695_saves_dec.zip` contains two gameplay entries: main slot 9 and its
console backup. There is **one generation and one unique payload**. Neither a
backup twin nor comparison of the archive to itself is a controlled event pair.
No churn masks or invented event fixtures were added.

Reproduction from the repository root:

```powershell
$archive='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts/PPSA10695_saves_dec.zip'
dotnet run --project tools/SaveToolkit.SaveDiff -- compare $archive $archive --semantic atomic-heart --all --file ue4savegame.dpx.sav

$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/AtomicHeart/SaveToolkit.Plugins.AtomicHeart.Tests
```

Observed F1 result: 2 unchanged files, 1 identical backup twin, 0 changed bytes,
0 semantic changes and 0 decode errors. F1 short-circuits identical files; the
sample tests additionally decode and walk both plugin views with a 20,000-section
budget. Both expose 9,107 scalar values without truncation or duplicate-path
warnings, and their semantic diff is empty.

An entirely authored fixture reorders two tagged properties while changing only
one `Amount` value. F1 reports exactly that value change, with no additions or
removals. This tests the adapter; it does not prove gameplay semantics.

## Decoding and boundaries

All 14 compressed chunks decode through `SaveToolkit.Codecs`' managed Kraken
implementation: 13 × 131,072 + 47,455 = 1,751,391 bytes. The inner array prefix is
1,751,387, exactly the remaining decoded length. Frame duplicate lengths, exact
codec input/output consumption, aggregate size and the outer SHA-1 are checked.

Root UObject tags occupy the beginning at offset 4; `None` begins at `0x8B`,
followed by a zero int32. Manual records begin at `0x98`. Five subsystem records
end at `0x129B`; the main level has 70 actors. The sublevel count at `0x11D65`
is 261. Across 262 levels there are 3,502 actors and 556 components, plus the five
subsystems: **4,063 indexed objects**. The final level ends exactly at 1,751,391.

Each index entry retains parent, kind, both class-related strings, name, full
record span, data span, tags, inspected properties and any opaque tail. Both
class strings are parsed as FStrings: some nonempty second strings occur even
when the first is `None`. Interpreting every second string as a zero int32 fails
the real corpus. Stock SaveExtension's GameInstance/filter/level-script sequence
is not substituted for this measured fork.

The 90 inspected slots comprise 12 ResourcesList, 7 SpecialResourcesList,
5 AmmoList, 10 AuxList, 50 QuestItemsList and 6 ItemsGrid entries. Counts include
empty slots; they do not establish capacity limits or player ownership. The
manual six-ID inventory prefix is separate from quantities. Storage's raw
properties remain inspectable even though it has no populated inspected slots.

A separately authored scratch Python grammar reader consumed the shared-decoded
stream to the same end and independently counted those records. Its input came
from the shared codec: **independent grammar inspection, not a second decoder**.
No scratch program, decoded dump or private sample was added to the repository.

## Phase status and limits

- Phase 1 implementation: managed decoding, exact length checks, manual grammar,
  bounded indexes, F8 search, span/opaque inspection and F1 corpus evidence done.
  Independent codec agreement remains an open acceptance item.
- Phase 2 partial: explicit serialized inventory fields, exact asset joins, unknown
  retention and an attributed 20-entry F2 public-label pack. Controlled gameplay
  observations and wider build/NG+/DLC coverage remain open.
- F4 integration: existing populated references now accept same-category catalog
  assets whose unambiguous full paths occur in the input. Unmapped/empty references
  remain read-only; their quantities may be edited when explicitly signed/populated.
- Phase 3 implementation: explicit Amount edits plus F6 selected/document target
  quantities, bounded to original container maxima; stored changed chunks, retained
  unchanged compressed chunks, all affected lengths and SHA-1 repaired. No-op and
  complete undo serialize byte-identically. `Serialize()` decodes/reparses output
  and compares all intended plain bytes. Maturity moved to Experimental only after
  sample-backed edit conformance passed.
- Phases 4–5: no allocation, structural move/split/removal, skill/loadout, event
  reward or progression writer exists. Their opaque/dependent state cannot safely
  be reconstructed. No writes are held off merely for missing owner evidence.
  No PS5 acceptance claim or Stable promotion.

Validation: **46 tests passed, 0 failed, 0 skipped**, with SAVETOOLKIT_ARTIFACTS set
in the test command. API build passed with 0 warnings and 0 errors. The suite
includes complete lazy traversal, catalog hashes/attribution, malformed inner
counts and lengths with valid SHA-1, synthetic truncations, 500 seeded inner
mutations, alternative-codec rejection, native-tail preservation, query paging,
duplicate-property bindings and byte-identical serialization of both real files.

## Write-round preservation and independent output checks

Each eligible sample Amount is edited, serialized and reopened. F1's `ByteDiffer`
finds changed decoded bytes only inside that explicit integer payload. All eligible
same-category reference pairs are exercised, including FString growth/shrinkage.
After aligning the changed scalar, F1 permits differences only in its explicit
enclosing length fields; every unknown/native byte is retained. Additional checks
compare record identities, leaf payloads, opaque tails, Amount and ItemId. Combined
quantity/reference edits and chunk growth/shrinkage/new-frame boundaries pass.

A separately authored literal-block oracle parses output framing without the
production output parser or `OodleCodec.Decompress`. New streams must contain
exactly `CC 06` followed by the declared bytes. Reused compressed streams must be
byte-identical to their original stream and use the original decoded baseline.
The resulting aggregate equals the intended edit, with exact array length and
SHA-1. This independently validates **new stored output**; it does not independently
decode the original compressed Kraken corpus. That remains an explicit acceptance
item in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).

Core DocumentSession tests verify individual Amount/reference undo/redo,
whole-selected-batch undo as one group, redo byte identity, rejected direct planned
action invocation, stale/single-use preview handling and atomic invalid batches.
Empty selections never mean document scope. All writable fields remain Inferred;
fields, outcomes, action descriptions and previews warn that the game may reject
the save because PS5 stored-block acceptance and inner integrity are unproven.

See [README.md](README.md) for the exact owner capture/console checklist, source
attribution, format safety limits and remaining SDK/evidence requests.
