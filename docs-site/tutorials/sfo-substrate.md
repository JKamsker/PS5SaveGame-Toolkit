---
title: Patch an existing SFO value without resizing it
description: Use the lossless ParamSfoDocument substrate for identity-bound, strict UTF-8 in-place plans.
---

# Patch an existing SFO value without resizing it

`ParamSfoDocument` is a bounded substrate for one narrow operation: replace an existing UTF-8
string in its reserved slot. It parses and retains every source byte, exposes read-only entry
metadata, and returns a new byte array from an identity-bound pure plan. It never creates a key,
resizes a slot, mutates the parsed document or claims ownership of a system output.

The focused characterization suite is
[`ParamSfoPatchTests.cs`](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/tests/SaveToolkit.Core.Tests/ParamSfoPatchTests.cs):

```powershell
dotnet test --project tests/SaveToolkit.Core.Tests --filter-method '*ParamSfoPatchTests*'
```

## Parse with finite limits

```csharp
var document = ParamSfoDocument.Parse(source, ParamSfoPatchLimits.Default);
var title = document.Entries.Single(entry => entry.Key == "TITLE");

Assert.Equal(source, document.OriginalBytes.ToArray());
Assert.Equal("TITLE", title.Key);
Assert.True(title.MaximumLength >= title.Length);
```

The default limits are a 1 MiB file, 64 KiB reserved entry, 1024 key bytes and 4 KiB encoded
replacement including its required NUL. Parsing rejects truncated headers/tables, unaligned or
overlapping ranges, duplicate keys, invalid lengths, oversized keys/slots and malformed table
regions. `Entries`, `ValueBytes`, `ReservedBytes` and `RawIndexBytes` are defensive/read-only
views. `OriginalFileSha256` fingerprints the complete imported bytes.

## Plan, then apply a pure replacement

`PlanExistingUtf8` requires a nonempty existing key, a supported UTF-8 format and replacement text
without an embedded NUL. It counts bytes with strict UTF-8 and checks both the reserved slot and
`MaxEncodedValueBytes` before returning an immutable `ParamSfoPatchPlan`.

```csharp
var plan = document.PlanExistingUtf8("TITLE", "Hi");
var output = document.Apply(plan).ToArray();

Assert.Equal(source.Length, output.Length);
Assert.True(output.SequenceEqual(document.OriginalBytes.ToArray()) == false);
Assert.True(plan.OriginalFileSha256 == document.OriginalFileSha256);
```

Applying a plan verifies the complete-file fingerprint, entry index/key/format/offset/length/
capacity, entry fingerprint and original text again. A forged, stale, foreign or wrong-format plan
throws `ParamSfoPatchException` before output is returned. A no-op plan (`plan.IsNoOp`) returns a
byte-identical copy and leaves `OriginalBytes` unchanged.

The patch changes only the index entry's logical length and the target slot's encoded bytes plus
its NUL. Reserved suffix bytes, sibling entries, alignment, unknown trailing bytes and the total
file length remain unchanged. Strict UTF-8 counts bytes rather than UTF-16 characters, so `é` and
`😀` consume their actual encoded widths; a lone surrogate, embedded NUL or capacity overflow is
rejected before encoding.

## Keep ownership outside this substrate

This API is a **pure patching substrate**, not the eventual exact-output owner for a game:

- A plugin or host must decide which existing system file is in scope, preserve its path and
  sibling relationships, and verify the complete candidate output after applying the plan.
- `ParamSfoDocument` does not synchronize gameplay state into `SUBTITLE`, does not create or resize
  entries and does not prove that a save manager caption should change.
- The separate system-output ownership foundation is now shipped, but SDK-10 remains open until
  controlled evidence shows that a full-byte caption output is required and the later SuckerPunch
  consumer lands. Do not publish an “owned system output” journey from this pure substrate.
- Export and document history remain host responsibilities. A pure plan can be included in an
  undoable host operation, but the substrate itself has no I/O, session or export side effects.

Read the [SFO SDK reference](../reference/index.md) for generated members, then return to
[working with saves](../guides/working-with-saves.md) for revision-bound review and export rules.
