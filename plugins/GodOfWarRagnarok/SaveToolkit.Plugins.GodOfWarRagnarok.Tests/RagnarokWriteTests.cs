using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokWriteTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryPrimarySupportsBoundedEditsReopenAndOnlyIntendedByteChanges()
    {
        var files = SampleSaves.Load("PPSA08330").AllDiskFiles.Where(file => !file.Directory.IsBackup &&
            file.FileName.StartsWith("GOWRSAVE", StringComparison.Ordinal)).ToArray();
        Assert.Equal(76, files.Length);
        var editedCounters = 0;
        foreach (var file in files)
        {
            var original = file.ReadAllBytes();
            var index = RagnarokRecordIndex.Parse(original, false);
            var model = new RagnarokWrites(original.ToArray(), index, new RagnarokReferences(SaveToolkit.Sdk.References.IReferenceContext.Empty));
            using var document = (RagnarokDocument)new GodOfWarRagnarokPlugin().Open(file);
            var fields = model.Counters.Select(counter => document.CounterField(counter.Offset)).ToArray();
            Assert.All(fields, field =>
            {
                Assert.False(field.Spec.ReadOnly);
                Assert.Equal(Confidence.Inferred, field.Confidence);
                Assert.Contains(RagnarokWrites.IntegrityWarning, field.Description);
                Assert.Equal(RagnarokWrites.IntegrityWarning, field.Warning);
            });
            var counters = model.Counters.Where(counter => counter.Imported > 0).ToArray();
            var edits = counters.Select(counter => new ValueEdit(counter.ValueId, FieldValue.Of((long)counter.Imported - 1))).ToArray();
            var result = document.ApplyEdits(edits);
            Assert.True(result.Succeeded);
            Assert.Equal(edits.Length, result.Changes.Count);
            Assert.Contains(RagnarokWrites.IntegrityWarning, result.Warnings);
            var changed = Assert.Single(document.Serialize()).Content.ToArray();
            var ranges = counters.Select(counter => (counter.Offset, sizeof(uint))).ToArray();
            AssertOnlyRangesChanged(original, changed, ranges);
            using var reopened = (RagnarokDocument)new GodOfWarRagnarokPlugin().Open(
                MemorySaveSet.SingleFile(file.FileName, changed, "PPSA08330"));
            foreach (var counter in counters)
            {
                var field = reopened.CounterField(counter.Offset);
                Assert.Equal((long)counter.Imported - 1, field.Value.AsInt64());
                Assert.Equal(counter.ValueId, field.ValueId);
            }

            Assert.True(document.ApplyEdits(result.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(document.ApplyEdits(edits).Succeeded);
            Assert.Equal(changed, Assert.Single(document.Serialize()).Content.ToArray());
            editedCounters += edits.Length;
        }

        output.WriteLine($"Edited, reparsed and byte-diff checked {editedCounters} positive counters over {files.Length} primary records.");
    }

    [Fact]
    public void EveryCounterRejectsWrongTypesOutOfRangeAndMixedInvalidBatches()
    {
        var file = Sample();
        var bytes = file.ReadAllBytes();
        var model = new RagnarokWrites(bytes, RagnarokRecordIndex.Parse(bytes, false),
            new RagnarokReferences(SaveToolkit.Sdk.References.IReferenceContext.Empty));
        using var document = (RagnarokDocument)new GodOfWarRagnarokPlugin().Open(file);
        foreach (var counter in model.Counters)
        {
            _ = document.CounterField(counter.Offset);
            foreach (var invalid in new[] { FieldValue.Of(-1L), FieldValue.Of((long)counter.Imported + 1),
                FieldValue.Of((ulong)uint.MaxValue + 1), FieldValue.Of("1"), FieldValue.Of(1.0), FieldValue.Of(true), FieldValue.Null })
            {
                Assert.False(document.ApplyEdits([new(counter.ValueId, invalid)]).Succeeded);
            }
        }

        var positive = model.Counters.First(counter => counter.Imported > 0);
        Assert.False(document.ApplyEdits([new(positive.ValueId, FieldValue.Of(0L)), new("unknown", FieldValue.Of(0L))]).Succeeded);
        _ = document.BuildView();
        Assert.False(document.ApplyEdits([new(positive.ValueId, FieldValue.Of(0L)), new("metadata/location", FieldValue.Of("changed"))]).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void LiveQueriesSortFilterAndAliasesKeepCanonicalBindingsAfterEdits()
    {
        var file = Sample();
        using var document = new GodOfWarRagnarokPlugin().Open(file);
        var queryable = (IQueryableDocument)document;
        var query = new QueryableCollectionQuery { Text = "Hacksilver", Filters =
            new Dictionary<string, string> { ["container"] = "Hero wallet (inferred)" } };
        var row = Assert.Single(queryable.Query("resources", query).Rows);
        var field = Assert.IsType<FieldNode>(row.Cells["quantity"]);
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(0L))]).Succeeded);
        var filtered = queryable.Query("resources", query with { Filters = new Dictionary<string, string>
            { ["container"] = "Hero wallet (inferred)", ["quantity"] = "0" } });
        Assert.Equal(0, Assert.IsType<FieldNode>(Assert.Single(filtered.Rows).Cells["quantity"]).Value.AsInt32());
        var sorted = queryable.Query("resources", new() { SortColumn = "quantity", Descending = true, Limit = 200 });
        var values = sorted.Rows.Select(r => Assert.IsType<FieldNode>(r.Cells["quantity"]).Value.AsUInt64()).ToArray();
        Assert.True(values.Zip(values.Skip(1)).All(pair => pair.First >= pair.Second));
        var legacy = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("wallet/0")));
        Assert.Contains(legacy.Rows, r => r.Cells["quantity"] is FieldNode candidate && candidate.ValueId == field.ValueId && candidate.Value.AsInt32() == 0);
        // Diagnostic aliases must never replace the writable canonical binding.
        _ = document.ExpandNode("raw-page/111");
        _ = document.ExpandNode("anchors/0");
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void SerializeReparsesOutputAndRejectsAnUnintendedMarkerCollision()
    {
        var bytes = RagnarokFormatTests.SyntheticGameplay();
        // The legacy fixture has one bounded row. Give it a quantity ceiling that allows the
        // low wallet-marker word, with the matching high word in the preserved metadata.
        const int quantityOffset = 0x51C;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(quantityOffset), uint.MaxValue);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(quantityOffset + sizeof(uint)), 0x00004895);
        using var document = (RagnarokDocument)new GodOfWarRagnarokPlugin().Open(MemorySaveSet.SingleFile("GOWRSAVE0", bytes, "PPSA08330"));
        var field = document.CounterField(quantityOffset);
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(0xC643225FL))]).Succeeded);
        Assert.Throws<SaveFormatException>(() => document.Serialize());
    }

    internal static SaveToolkit.Core.Storage.DiskSaveFile Sample() => SampleSaves.Load("PPSA08330").AllDiskFiles
        .First(file => !file.Directory.IsBackup && file.FileName == "GOWRSAVE0" && file.Directory.Generation == 0);

    internal static void AssertOnlyRangesChanged(byte[] original, byte[] changed, params (int Offset, int Length)[] allowed)
    {
        var diff = ByteDiffer.Compare(original, changed, options: new() { MergeGap = 0, MaxRanges = 1000 });
        Assert.Equal(original.Length, changed.Length);
        Assert.False(diff.Truncated);
        Assert.True(diff.ChangedBytes > 0);
        Assert.All(diff.Ranges, range => Assert.Contains(allowed, span => range.Offset >= span.Offset && range.End <= span.Offset + span.Length));
    }
}
