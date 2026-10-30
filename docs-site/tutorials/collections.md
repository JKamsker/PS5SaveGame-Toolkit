---
title: Query a bounded collection
description: Extend the synthetic fixture and verify that search, native sorting and pagination agree.
---

# Query a bounded collection

Start with the read-only plugin from [create your first plugin](first-plugin.md). You will add one synthetic record and test that a filtered query finds it without changing serialization.

## 1. Extend the synthetic fixture

Open `ExampleGameDocumentTests.cs`. Its `ExampleBytes` fixture has three values: `2`, `10`, `20`. Change the count from `3, 0, 0, 0` to `4, 0, 0, 0`, and insert `30, 0, 0, 0` before the unknown trailing `0xAA, 0xBB`:

```csharp
// STKDEMO1, count=4, values=2/10/20/30, and two unknown trailing bytes.
private static byte[] ExampleBytes =>
    [.. "STKDEMO1"u8, 4, 0, 0, 0, 2, 0, 0, 0, 10, 0, 0, 0,
        20, 0, 0, 0, 30, 0, 0, 0, 0xAA, 0xBB];
```

Update the existing `page.TotalCount` assertion from `3` to `4`. Keep the numeric-sort assertion: the second value must remain `10`, not `2` as lexical sorting would suggest.

## 2. Assert the complete query result

Inside `SyntheticView_QueriesCompleteModelAndPreservesBytes`, after the existing filtered-query assertions, add:

```csharp
var added = queryable.Query(collection.Id, new() { Text = "30", Limit = 1 });
Assert.Equal(4, added.TotalCount);
Assert.Equal(1, added.FilteredCount);
var addedField = Assert.IsType<FieldNode>(Assert.Single(added.Rows).Cells["value"]);
Assert.Equal(FieldValue.Of(30), document.GetValue(addedField.ValueId));
```

Leave the final `Serialize` assertions in place. They prove that the new fixture and unknown tail round-trip exactly. Run:

```powershell
dotnet test --project plugins/ExampleGame/SaveToolkit.Plugins.ExampleGame.Tests
```

The synthetic tests must pass; sample-dependent skips remain expected without external samples.

## 3. Understand the collection contract

<xref:SaveToolkit.Sdk.Collections.IQueryableDocument> publishes stable collection metadata and handles bounded queries. <xref:SaveToolkit.Sdk.Collections.InMemoryCollection`1> applies search/filtering and native-value ordering over the **complete bounded model**, then renders the requested page. Do not filter only the visible rows.

`TotalCount` describes all records. `FilteredCount` describes the matching visible records across pages. Row keys must be unique and stable, and values rendered as fields must retain their published binding IDs. Larger formats may need a different indexed backing model; never allocate from an untrusted count without a format-specific bound.

For a collection with an empty-row policy, resolve `IncludeEmpty` before paging. Real structural edits also need identities that survive insertion, removal and reorder; the fixed-offset demonstration keys are insufficient there. See the [collection guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/PLUGIN_GUIDE.md#pagedqueryable-collections) for the authoritative contract.

## Next: enrich labels without inventing ownership

Reference data can add names, descriptions, icons and provenance to existing raw IDs. It does not prove that an item is owned, eligible or safe to grant. Start with <xref:SaveToolkit.Sdk.References.IReferenceContext> and the [reference-pack guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/PLUGIN_GUIDE.md#reference-packs) before introducing a pack or picker. Keep unavailable data usable as raw IDs and retain source/licence attribution.

From here, choose the contract that matches the format: [lossless JSON](lossless-json.md) for
exact scalar spelling, [canonical field references](field-references.md) for repeated projections,
or the [existing-entry SFO substrate](sfo-substrate.md) for strict UTF-8 metadata patches. For
reviewable bulk changes, return to the [save workflow](../guides/working-with-saves.md) and inspect
the preview, skipped rows, warnings and Changes panel before applying an action.
