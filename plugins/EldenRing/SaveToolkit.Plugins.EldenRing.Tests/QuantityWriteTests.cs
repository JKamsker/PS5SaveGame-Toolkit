using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class QuantityWriteTests
{
    [Theory]
    [InlineData("0x400050F0", 11U, 12U)]
    [InlineData("0x40002774", 2U, 3U)]
    public void SampleQuantity_PersistsOnlyOneWordAndSupportsSessionUndoRedo(string rawId, uint next, uint ceiling)
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        var character = SemanticReader.Read(original, EldenRingParser.Parse(original).Slots[0]);
        var entry = character.Containers.SelectMany(container => container.Entries).Single(entry => entry.RawId == rawId);
        using var session = WriteTestSupport.Session(file, out var document);
        var field = WriteTestSupport.Quantity(session, "held", rawId);
        Assert.False(field.Spec.ReadOnly);
        Assert.Equal(Confidence.Inferred, field.Confidence);
        Assert.Equal((Int128)ceiling, Assert.IsType<IntegerSpec>(field.Spec).Max);
        Assert.Contains("may reject", field.Warning);
        Assert.True(session.ApplyEdits([WriteTestSupport.Incoming(field.ValueId, next)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var expected = original.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(entry.Offset + sizeof(uint)), next);
        WriteTestSupport.EqualBytes(expected, document);
        WriteTestSupport.OnlyRanges(original, expected, (entry.Offset + sizeof(uint), sizeof(uint)));
        Assert.Equal(next, WriteTestSupport.Quantity(session, "held", rawId).Value.AsUInt32());
        var filtered = session.QueryCollection("slots/0/inventory/held", new()
            { Text = rawId, Filters = new Dictionary<string, string> { ["quantity"] = next.ToString(System.Globalization.CultureInfo.InvariantCulture) } }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(filtered.Rows); // Query/filter reads current bytes, not the opening snapshot.
        using var reopened = WriteTestSupport.Reopen(document);
        var reopenedField = Assert.IsType<FieldNode>(Assert.Single(((IQueryableDocument)reopened)
            .Query("slots/0/inventory/held", new() { Text = rawId }).Rows).Cells["quantity"]);
        Assert.Equal(next, reopenedField.Value.AsUInt32());
        Assert.True(session.Undo().Ok);
        WriteTestSupport.EqualBytes(original, document);
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        WriteTestSupport.EqualBytes(expected, document);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(13L)]
    [InlineData(long.MaxValue)]
    public void InvalidQuantity_RejectsWholeBatch(long next)
    {
        var fixture = new SemanticFixture();
        fixture.Row("held", "common", 0, 0xB00050F0, 12, 1);
        using var document = fixture.Save.Open();
        _ = document.BuildView();
        var row = Assert.Single(((IQueryableDocument)document).Query("slots/0/inventory/held", new()).Rows);
        var field = Assert.IsType<FieldNode>(row.Cells["quantity"]);
        Assert.False(document.ApplyEdits([new("slots/0/name", FieldValue.Of("Renamed")), new(field.ValueId, FieldValue.Of(next))]).Succeeded);
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of("2"))]).Succeeded);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("key")]
    [InlineData("zero")]
    [InlineData("above-ceiling")]
    [InlineData("duplicate")]
    [InlineData("count-mismatch")]
    [InlineData("container-managed")]
    public void UnsupportedRows_RemainProtectedWithoutRepair(string reason)
    {
        var fixture = new SemanticFixture();
        var handle = reason switch { "unknown" => 0xB000FFFFU, "container-managed" => 0xB000251CU, _ => 0xB00050F0U };
        fixture.Row("held", reason == "key" ? "key" : "common", 0, handle,
            reason == "zero" ? 0U : reason == "above-ceiling" ? 13U : 1U, 1);
        if (reason == "duplicate") fixture.Row("held", "common", 1, handle, 2, 2);
        if (reason == "count-mismatch") fixture.Save.Write32(fixture.Held, 2);
        using var document = fixture.Save.Open();
        var rows = ((IQueryableDocument)document).Query("slots/0/inventory/held", new()).Rows;
        Assert.Equal(reason == "duplicate" ? 2 : 1, rows.Count);
        Assert.Equal(rows.Count, rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (var row in rows)
        {
            var field = Assert.IsType<FieldNode>(row.Cells["quantity"]);
            Assert.True(field.Spec.ReadOnly);
            var outcome = document.ApplyEdits([new(field.ValueId, FieldValue.Of(2L))]);
            Assert.False(outcome.Succeeded);
            Assert.NotEmpty(outcome.Errors);
            Assert.Empty(outcome.Changes);
            WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
        }
    }

    [Fact]
    public void ExistingStorageStack_ChangesQuantityWithoutCountsHandlesOrHeldState()
    {
        var fixture = new SemanticFixture();
        fixture.Row("storage", "common", 19, 0xB00050F0, 7, 99);
        fixture.Row("held", "common", 1, 0xB00050F0, 12, 1);
        using var document = fixture.Save.Open();
        var row = Assert.Single(((IQueryableDocument)document).Query("slots/0/inventory/storage", new()).Rows);
        var field = Assert.IsType<FieldNode>(row.Cells["quantity"]);
        Assert.False(field.Spec.ReadOnly);
        var edited = document.ApplyEdits([new(field.ValueId, FieldValue.Of(8L))]);
        Assert.True(edited.Succeeded);
        var expected = fixture.Save.Bytes.ToArray();
        var quantityOffset = fixture.Storage + sizeof(uint) + 19 * 3 * sizeof(uint) + sizeof(uint);
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(quantityOffset), 8);
        WriteTestSupport.EqualBytes(expected, document);
        WriteTestSupport.OnlyRanges(fixture.Save.Bytes, expected, (quantityOffset, sizeof(uint)));
        using var reopened = WriteTestSupport.Reopen(document);
        var restored = Assert.IsType<FieldNode>(Assert.Single(((IQueryableDocument)reopened)
            .Query("slots/0/inventory/storage", new()).Rows).Cells["quantity"]);
        Assert.Equal(8U, restored.Value.AsUInt32());
        Assert.True(document.ApplyEdits(edited.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
        Assert.True(document.ApplyEdits(edited.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray()).Succeeded);
        WriteTestSupport.EqualBytes(expected, document);
    }

    [Fact]
    public void Serialize_RevalidatesDeepContainerStructureBeforeReturningOutput()
    {
        var fixture = new SemanticFixture();
        var parsed = EldenRingParser.Parse(fixture.Save.Bytes);
        using var document = new EldenRingDocument("sce_sdmemory/memory.dat", fixture.Save.Bytes, parsed,
            SaveToolkit.Sdk.References.IReferenceContext.Empty);
        fixture.Save.Write32(fixture.Held, uint.MaxValue);
        Assert.Throws<SaveFormatException>(() => document.Serialize());
    }
}
