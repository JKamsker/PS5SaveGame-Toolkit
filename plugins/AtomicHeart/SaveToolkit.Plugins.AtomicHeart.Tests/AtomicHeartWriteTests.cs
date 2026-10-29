using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartWriteTests
{
    [Fact]
    public void EveryEligibleAmountPersistsWithOnlyItsExplicitPlainPayloadChanged()
    {
        var file = WriteTestData.Sample();
        var original = file.ReadAllBytes();
        var outer = SaveExtensionFile.Parse(original);
        var index = SlotDataDecoder.Decode(original, outer);
        var state = new InventoryState(index, WriteTestData.References().Catalog("items"));
        var eligible = state.Items.Where(state.CanSetAmount).ToArray();
        Assert.NotEmpty(eligible);
        foreach (var item in eligible)
        {
            using var document = WriteTestData.Plugin.Open(file, WriteTestData.References());
            var field = WriteTestData.Field(WriteTestData.Table(document).Rows.Single(r => r.Key == item.Key), "amount");
            Assert.False(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Contains("game may reject", field.Description, StringComparison.Ordinal);
            var next = FieldValue.Of(item.Amount == 1 ? 0 : item.Amount - 1);
            var outcome = document.ApplyEdits([new(field.ValueId, next)]);
            Assert.True(outcome.Succeeded);
            Assert.Contains(outcome.Warnings, w => w.Contains("game may reject", StringComparison.Ordinal));
            var output = WriteTestData.Output(document);
            var plain = WriteTestData.IndependentDecode(original, output);
            var diff = ByteDiffer.Compare(index.Plain, plain, options: new() { MergeGap = 0 });
            Assert.True(diff.HasChanges);
            Assert.False(diff.Truncated);
            Assert.All(diff.Ranges, r => Assert.True(r.Offset >= item.Quantity.Payload.Offset && r.End <= item.Quantity.Payload.End));
            using var reopened = WriteTestData.Open(output);
            Assert.Equal(next, reopened.GetValue(field.ValueId));
            Assert.Equal(output, WriteTestData.Output(reopened));
            Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
            Assert.Equal(original, WriteTestData.Output(document));
        }
        TestContext.Current.TestOutputHelper!.WriteLine($"Edited and independently checked {eligible.Length} populated Amount fields.");
    }

    [Fact]
    public void AllEligibleReferencePairsPersistAndRetainEveryUnrelatedPayloadAndUnknownByte()
    {
        var file = WriteTestData.Sample();
        var original = file.ReadAllBytes();
        var index = SlotDataDecoder.Decode(original, SaveExtensionFile.Parse(original));
        var context = WriteTestData.References();
        var state = new InventoryState(index, context.Catalog("items"));
        var replacements = 0;
        var grew = false;
        var shrank = false;
        foreach (var item in state.Items.Where(state.CanReplace))
        foreach (var candidate in state.Items.Where(InventoryState.Populated).Select(i => InventoryState.ShortId(i.RawAsset)).Distinct()
            .Where(id => id != InventoryState.ShortId(item.RawAsset) && state.Eligibility(item, id).Eligible))
        {
            using var document = WriteTestData.Plugin.Open(file, context);
            var field = WriteTestData.Field(WriteTestData.Table(document).Rows.Single(r => r.Key == item.Key), "asset");
            var outcome = document.ApplyEdits([new(field.ValueId, FieldValue.Of(candidate))]);
            Assert.True(outcome.Succeeded);
            Assert.True(outcome.StructureChanged);
            var output = WriteTestData.Output(document);
            var plain = WriteTestData.IndependentDecode(original, output);
            var decoded = new RecordReader(plain).Read();
            var after = new InventoryState(decoded, context.Catalog("items"));
            var replacement = after.Items.Single(i => i.Key == item.Key);
            Assert.Equal(candidate, InventoryState.ShortId(replacement.RawAsset));
            Assert.Equal(item.Amount, replacement.Amount);
            Assert.Equal(item.InstanceId, replacement.InstanceId);
            WritePreservation.AssertReferenceDiff(index, decoded, item, replacement);
            AssertUnrelatedRetained(index, decoded, item.Asset.Payload);
            using var reopened = WriteTestData.Open(output);
            Assert.Equal(FieldValue.Of(candidate), reopened.GetValue(field.ValueId));
            Assert.Equal(output, WriteTestData.Output(reopened));
            Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
            Assert.Equal(original, WriteTestData.Output(document));
            grew |= plain.Length > index.Plain.Length;
            shrank |= plain.Length < index.Plain.Length;
            replacements++;
        }
        Assert.True(replacements > 1);
        Assert.True(grew);
        Assert.True(shrank);
        TestContext.Current.TestOutputHelper!.WriteLine($"Checked {replacements} asset replacements, including growth and shrinkage.");
    }

    [Fact]
    public void InvalidScalarEditsRejectTheWholeBatchAndEmptySlotsRemainReadOnly()
    {
        using var document = WriteTestData.Plugin.Open(WriteTestData.Sample(), WriteTestData.References());
        var table = WriteTestData.Table(document);
        var field = table.Rows.Select(r => WriteTestData.Field(r, "amount")).First(f => !f.Spec.ReadOnly);
        var spec = Assert.IsType<IntegerSpec>(field.Spec);
        foreach (var invalid in new[] { FieldValue.Of(-1), FieldValue.Of(spec.Max + 1), FieldValue.Of("1"), FieldValue.Of(1.5) })
        {
            Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(0)), new(field.ValueId, invalid)]).Succeeded);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
        }
        Assert.Contains(table.Rows, r => WriteTestData.Field(r, "amount").Spec.ReadOnly);
        var asset = table.Rows.Select(r => WriteTestData.Field(r, "asset")).Single(f => f.Value.AsString() == "DA_Item_Shved");
        foreach (var invalid in new[] { "None", "DA_Unknown_Item", "DA_Item_Synthetic_Material_Resource", "" })
            Assert.False(document.ApplyEdits([new(asset.ValueId, FieldValue.Of(invalid))]).Succeeded);
        var differentCategory = table.Rows.Select(r => WriteTestData.Field(r, "asset")).First(f => f.Value.AsString().Contains("Ammo", StringComparison.Ordinal));
        Assert.False(document.ApplyEdits([new(asset.ValueId, differentCategory.Value)]).Succeeded);
        Assert.Equal(WriteTestData.Sample().ReadAllBytes(), WriteTestData.Output(document));
    }

    private static void AssertUnrelatedRetained(RecordIndex before, RecordIndex after, ByteSpan edited)
    {
        Assert.Equal(before.Objects.Select(r => r.Id), after.Objects.Select(r => r.Id));
        foreach (var record in before.Objects)
        {
            var reopened = after.Objects.Single(r => r.Id == record.Id);
            Assert.Equal(before.Plain.AsSpan(record.UnknownTail.Offset, record.UnknownTail.Length).ToArray(),
                after.Plain.AsSpan(reopened.UnknownTail.Offset, reopened.UnknownTail.Length).ToArray());
            if (record.Payload.Offset <= edited.Offset && record.Payload.End >= edited.End)
            {
                foreach (var property in Leaves(record.Properties).Where(p => p.Payload != edited))
                {
                    var relative = Leaves(record.Properties).ToList().IndexOf(property);
                    var next = Leaves(reopened.Properties).ElementAt(relative);
                    Assert.Equal(before.Plain.AsSpan(property.Payload.Offset, property.Payload.Length).ToArray(),
                        after.Plain.AsSpan(next.Payload.Offset, next.Payload.Length).ToArray());
                }
            }
            else if (!(record.Span.Offset <= edited.Offset && record.Span.End >= edited.End))
                Assert.Equal(before.Plain.AsSpan(record.Span.Offset, record.Span.Length).ToArray(),
                    after.Plain.AsSpan(reopened.Span.Offset, reopened.Span.Length).ToArray());
        }
    }

    private static IEnumerable<TaggedProperty> Leaves(IReadOnlyList<TaggedProperty> properties) =>
        properties.SelectMany(p => p.Children.Count == 0 ? [p] : Leaves(p.Children));
}
