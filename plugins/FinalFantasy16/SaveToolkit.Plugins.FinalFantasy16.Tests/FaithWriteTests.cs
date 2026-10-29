using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithWriteTests
{
    private readonly FinalFantasy16Plugin _plugin = new();

    [Theory]
    [InlineData("PPSA10665")]
    [InlineData("PPSA13803")]
    public void EveryNewWritableFieldPersistsWithOnlyItsRangeAndCrcAndSessionUndoRedo(string title)
    {
        var file = SampleFile(title);
        var original = file.ReadAllBytes();
        var container = FaithContainer.Parse(original);
        var map = new FaithWriteMap(original.ToArray(), container, FaithSchema.ForTitle(title), FaithInventory.Parse(original, container));
        var references = new ReferencePackStore([_plugin]).Pin(_plugin, title);
        using var document = _plugin.Open(file, references);
        using var session = new DocumentSession("ff16-write", file, _plugin, document, references: references);
        var fields = FaithViewFields.All(session).DistinctBy(field => field.ValueId)
            .Where(field => !field.Spec.ReadOnly && map.Find(field.ValueId) is not null).ToArray();
        Assert.True(fields.Length > 150);
        var exercised = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            // Edits rebuild the view and retire queried pages; emulate the client reloading this row.
            if (field.ValueId.StartsWith("inventory/", StringComparison.Ordinal))
                _ = session.QueryCollection(FaithInventoryView.CollectionId, new QueryableCollectionQuery
                    { Filters = new Dictionary<string, string> { ["slot"] = field.ValueId.Split('/')[1] } }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Contains("Not validated on console", field.Description);
            Assert.Contains("Not validated on console", field.Warning);
            Assert.NotEqual(Confidence.Verified, field.Confidence);
            // An observed constant (e.g. demo LB=0) has no experimental alternative yet.
            var next = Alternative(field, document, references);
            if (next is null)
            {
                var constant = Assert.IsType<IntegerSpec>(field.Spec);
                Assert.Equal(constant.Min, constant.Max);
                continue;
            }
            var invalid = field.Spec is ReferenceSpec ? FieldValue.Of("2147483648") : FieldValue.Of((long)int.MaxValue + 1);
            Assert.False(document.ApplyEdits([new ValueEdit(field.ValueId, invalid)]).Succeeded);
            Assert.False(session.ApplyEdits([Incoming(field.ValueId, invalid)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            var accepted = session.ApplyEdits([Incoming(field.ValueId, next.Value)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(accepted.Ok, $"{field.ValueId}: {string.Join(';', accepted.Errors.Select(error => error.Message))}");
            Assert.Contains(accepted.Warnings, warning => warning.Contains("Not validated on console", StringComparison.Ordinal));
            var output = Assert.Single(document.Serialize()).Content.ToArray();
            var storage = map.Find(field.ValueId)!;
            AssertOnlyRanges(original, output, [(storage.Offset, storage.Width)]);
            using var reopened = _plugin.Open(FaithSamples.WithBytes(file, output), references);
            _ = reopened.BuildView();
            Assert.Equal(next.Value, reopened.GetValue(field.ValueId));
            Assert.True(session.Undo().Ok, field.ValueId);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.False(session.State.CanUndo);
            Assert.True(session.Redo().Ok, field.ValueId);
            Assert.Equal(output, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Undo().Ok, field.ValueId);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            exercised.Add(field.ValueId.Split('/')[0]);
        }
        Assert.Subset(exercised, new HashSet<string> { "player", "context", "party", "user", "equipment", "skills", "inventory", "secondary" });
        Assert.Equal(8, exercised.Count);
        Assert.Equal(original, file.ReadAllBytes());
    }

    [Fact]
    public void IdentityReplacementsValidateTheWholeBatchAndUndoRestoresUnknownIds()
    {
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, FaithTestData.Items()));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        _ = document.BuildView();
        Assert.False(document.ApplyEdits([new("inventory/0/item", FieldValue.Of("3")),
            new("inventory/1/quantity", FieldValue.Of(42))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new("inventory/0/item", FieldValue.Of("3")),
            new("inventory/1/item", FieldValue.Of("1"))]).Succeeded); // Atomic swap is not a duplicate.
        Assert.Equal("3", document.GetValue("inventory/0/item").AsString());
        Assert.Equal("1", document.GetValue("inventory/1/item").AsString());
        var invalids = new[] { FieldValue.Of("0"), FieldValue.Of("99999"), FieldValue.Of("01"), FieldValue.Of(-1) };
        var swapped = Assert.Single(document.Serialize()).Content.ToArray();
        foreach (var value in invalids)
        {
            Assert.False(document.ApplyEdits([new("inventory/0/item", value)]).Succeeded);
            Assert.Equal(swapped, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData(900000)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void UnknownEquipmentCanBeReplacedAndRestoredWithoutChangingOtherArraysOrTrailingData(int originalId)
    {
        var equipment = new byte[FaithSchema.EquipmentLength];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(equipment, originalId);
        var bytes = FaithTestData.Container((FaithSchema.EquipmentType, FaithSchema.EquipmentVersion, equipment), (99, 1, new byte[8]));
        bytes = [.. bytes, 0x12, 0x34];
        FaithTestData.FixCrc(bytes);
        using var document = _plugin.Open(FaithTestData.File(bytes));
        _ = document.BuildView();
        Assert.True(document.CanEdit);
        Assert.True(document.ApplyEdits([new("equipment/0/0/item", FieldValue.Of("100029"))]).Succeeded);
        AssertOnlyRanges(bytes, Assert.Single(document.Serialize()).Content.ToArray(), [(32, sizeof(int))]);
        Assert.True(document.ApplyEdits([new("equipment/0/0/item", FieldValue.Of(FaithCatalog.Key(originalId)))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    internal static DiskSaveFile SampleFile(string title) => SampleSaves.Load(title).AllDiskFiles.First(file =>
        file.Directory.Name == "FAITHDATAAUTO" && file.FileName == "game-save-data00" && file.Directory.Generation == 0 && !file.Directory.IsBackup);

    internal static IncomingEdit Incoming(string id, FieldValue value) => new(id, value.Kind == FieldValueKind.Text
        ? JsonSerializer.SerializeToElement(value.AsString()) : JsonSerializer.SerializeToElement(value.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture)));

    internal static void AssertOnlyRanges(byte[] before, byte[] after, IReadOnlyList<(int Offset, int Width)> allowed)
    {
        var diff = ByteDiffer.Compare(before, after, options: new ByteDiffOptions
            { MergeGap = 0, MaxRanges = allowed.Sum(range => range.Width) + sizeof(uint) });
        Assert.Equal(before.Length, after.Length);
        Assert.False(diff.Truncated);
        Assert.True(diff.HasChanges);
        Assert.All(diff.Ranges, range => Assert.True(range.Offset >= FaithContainer.CrcOffset && range.End <= FaithContainer.CrcOffset + sizeof(uint) ||
            allowed.Any(item => range.Offset >= item.Offset && range.End <= item.Offset + item.Width), $"Unexpected changed range: {range.Offset}+{range.Length}"));
    }

    private static FieldValue? Alternative(FieldNode field, ISaveDocument document, IReferenceContext references)
    {
        if (field.Spec is IntegerSpec number)
            return field.Value.AsInteger() > number.Min ? FieldValue.Of(number.Min) :
                field.Value.AsInteger() < number.Max ? FieldValue.Of(number.Max) : null;
        var spec = Assert.IsType<ReferenceSpec>(field.Spec);
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(references.Catalog(spec.CatalogId));
        var records = catalog.Search(new ReferenceQuery { Limit = 200 }).Items.Concat(catalog.Search(new ReferenceQuery { Offset = 200, Limit = 200 }).Items);
        foreach (var record in records)
        {
            if (record.Key == field.Value.AsString()) continue;
            var result = document.ApplyEdits([new(field.ValueId, FieldValue.Of(record.Key))]);
            if (!result.Succeeded) continue;
            Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
            return FieldValue.Of(record.Key);
        }
        return null;
    }
}
