---
title: Change a Tsushima save caption and export its metadata
description: Preview a manual SUBTITLE edit within its UTF-8 capacity and retain the exact companion output.
---

# Change a Tsushima save caption and export its metadata

Use **Set save caption (manual, Experimental)** to replace an existing save caption. This is a
manual metadata edit with Inferred confidence. It changes this save directory's
`sce_sys/param.sfo` `SUBTITLE`; timer and difficulty remain unchanged. Automatic synchronization
between gameplay state and the caption is not established, and native console acceptance is
unverified.

## Open an eligible save

Import the complete decrypted directory and open its `dis.dat` or `memory.dat`. The caption
consumer supports Ghost of Tsushima (`PPSA02225`) with an editable recognized v47–v49 inventory.
The primary file must be at the directory root. Yōtei and inspection-only or unsupported layouts
do not offer this caption edit.

The same directory must contain exactly one `sce_sys/param.sfo` and an existing, valid UTF-8
`SUBTITLE` with a final NUL and enough reserved capacity. Missing files/keys, duplicate paths,
malformed SFO data, unsupported encodings, invalid terminators and an existing multiline caption
produce **Save caption unavailable: …**. Read the reason. The editor preserves the metadata;
this action cannot create a key, repair an SFO or enlarge its slot.

## Preview a single-line caption

1. On **Overview**, read the existing `param.sfo SUBTITLE` and open **Caption value and provenance**
   if you need its field details.
2. Choose **Set save caption (manual, Experimental)** and enter a single line.
3. Check the UTF-8 usage and remaining capacity, then select **Preview**.
4. Inspect the old/new caption, metadata-only warning and source/entry fingerprints. Fingerprints
   bind the plan to bytes; they do not prove what the game will display.
5. Select **Apply**, then [review the changed caption](metadata-review.md). Use **Undo** or **Redo**
   for the whole caption operation.

This action accepts **document scope only**. Inventory row selections and filtered-query scopes
are rejected. Apply caption and gameplay edits as separate operations. **Cancel** before Apply
leaves bytes unchanged. If the document changed, use **Preview again** after refresh; if refresh
failed, use **Retry document refresh** first. Reusing the existing text produces an empty plan,
with no dirty-state, revision or history change; ordinary action Apply is disabled for that plan.

## Fit the byte capacity

The hint counts UTF-8 payload bytes, not characters: `A` uses 1 byte, `é` uses 2 and `😀` uses 4.
Combining characters each consume bytes too. The caption capacity is the smaller of the existing
slot size and the 4096-byte writer limit, minus one byte reserved for the required NUL. A slot of
64 bytes therefore permits at most 63 payload bytes. The shown capacity already excludes the
terminator; do not subtract another byte.

Shorten text when the hint reports **over capacity**. Invalid Unicode must be corrected before
saving. Embedded NUL and CR/LF are rejected even if the byte count fits. A byte counter is not a
promise that every other format rule is satisfied. The server validates the caption again.

## Export both the primary and its owned output

The opened document owns this exact additional system-file path. A caption operation retains
the primary bytes and produces the complete SFO output; only the target entry's logical length
and replacement bytes plus NUL change. Reserved suffix bytes, sibling entries, unknown trailing
bytes and total file length are retained. Another open document cannot own the same SFO: for
example, opening both `dis.dat` and `memory.dat` in the same directory can conflict. Use the
document already owning it, or reopen in a fresh workspace. Different directories remain separate.

In **Export edited saves**, choose the scope deliberately:

| Export option | Caption journey result |
| --- | --- |
| Changed save folders | Includes the changed directory's complete imported folder, with its primary and SFO outputs applied. |
| Only changed files | Includes the document's rewritten outputs, including the unchanged primary and changed SFO. This is not a byte-diff-only package. |
| Whole dump | Includes every imported file with the reviewed outputs applied; also permits an unchanged export. |

The optional **Also update the console backup copy (sce_bu) when it matched the original** mirrors
only matching, byte-identical original paths in the paired same-title directory. A different or
independently owned backup is protected. It is not permission to overwrite every backup.

Check export warnings and paths, download to a new location and reimport to inspect the caption.
If source fingerprints no longer match, export is rejected; reimport the intended source and
review a fresh edit. A clean or no-op document has no output overrides; changed-only export
cannot download an unchanged workspace. Keep the original and test console restore/load on a
disposable slot separately.

## Evidence and limits

The production [caption consumer](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch/SuckerPunchCaption.cs)
uses the [SFO substrate](../tutorials/sfo-substrate.md).
[Eligibility](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch.Tests/CaptionEligibilityTests.cs),
[session/history/export](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch.Tests/CaptionSessionTests.cs)
and [ownership tests](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch.Tests/CaptionOwnershipTests.cs)
check unavailable states, no-op/stale behavior, full bytes, export scopes and protected backups.
The [caption browser journey](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/web/e2e/suckerpunch-caption-real.e2e.ts)
and [text-boundary cases](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/web/e2e/suckerpunch-caption-text-boundaries.e2e.ts)
exercise the production UI with authored fixtures. Their byte/history/reopen evidence does not
establish automatic gameplay synchronization, Yōtei support or console acceptance.
