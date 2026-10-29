using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class NullableFactTests
{
    [Fact]
    public void AbsentCatalogFactsAreTypedNullWhileSavedZeroStaysAnIntegerAndTracksUndo()
    {
        var file = ProgressionSampleTests.Files()[0];
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("nullable-facts", (DiskSaveFile)file, plugin, document, references: context);
        _ = session.BuildView();
        var absentRow = Assert.Single(session.QueryCollection("fact-catalog", new QueryableCollectionQuery
            { Filters = new Dictionary<string, string> { ["presence"] = "Catalog only / absent from save" }, Limit = 1 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows);
        var absent = Value(absentRow);
        Assert.True(absent.Value.IsNull);
        Assert.IsType<IntegerSpec>(Assert.IsType<NullableSpec>(absent.Spec).Inner);
        Assert.True(absent.Spec.ReadOnly);
        Assert.Contains("no saved value", absent.Description);
        Assert.Equal("null", JsonSerializer.Serialize(absent.Value));
        var zeroRow = Assert.Single(session.QueryCollection("fact-catalog", new QueryableCollectionQuery
            { Filters = new Dictionary<string, string> { ["value"] = "0" }, Limit = 1 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows);
        var zero = Value(zeroRow);
        Assert.Equal(FieldValue.Of(0L), zero.Value);
        Assert.Equal("\"0\"", JsonSerializer.Serialize(zero.Value));
        Assert.False(session.ApplyEdits([new(zero.ValueId, JsonSerializer.SerializeToElement("1"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.False(session.ApplyEdits([new(absent.ValueId, JsonSerializer.SerializeToElement("1"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);

        var parts = zeroRow.Key.Split('/');
        var originalId = $"facts/{parts[2]}/{parts[3]}";
        var raw = Assert.Single(session.QueryCollection("facts", new QueryableCollectionQuery { Filters = new Dictionary<string, string>
            { ["table"] = parts[2], ["hash"] = ReadOnlyInspectionSampleTests.Cell(zeroRow, "hash") }, Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows);
        Assert.Equal(originalId, raw.Key);
        Assert.True(session.ApplyEdits([new(originalId, JsonSerializer.SerializeToElement("1"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(FieldValue.Of(1L), document.GetValue(zero.ValueId));
        Assert.True(session.Undo().Ok);
        Assert.Equal(FieldValue.Of(0L), document.GetValue(zero.ValueId));
        Assert.True(session.Redo().Ok);
        Assert.Equal(FieldValue.Of(1L), document.GetValue(zero.ValueId));
        Assert.True(session.Undo().Ok);
        Assert.True(document.GetValue(absent.ValueId).IsNull);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void NullableNumericColumnsSortByIntegerValueAndKeepAbsentRowsDistinct()
    {
        var values = new[] { "100", "20", "", "0" };
        var collection = new InspectionCollection<string>(new QueryableCollection("null-test", "Nullable quantities",
            [new("value", "Value", new NullableSpec(IntegerSpec.UInt32))], values.Length), values,
            value => "value/" + value, new Dictionary<string, Func<string, string>> { ["value"] = value => value },
            value => new TableRow("value/" + value, new Dictionary<string, ViewNode>()));
        Assert.Equal(["value/", "value/0", "value/20", "value/100"], collection.Query(new QueryableCollectionQuery { SortColumn = "value" }).Rows.Select(row => row.Key));
        Assert.Equal(["value/100", "value/20", "value/0", "value/"], collection.Query(new QueryableCollectionQuery { SortColumn = "value", Descending = true }).Rows.Select(row => row.Key));
        Assert.Equal("value/", Assert.Single(collection.Query(new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["value"] = "" } }).Rows).Key);
    }

    private static FieldNode Value(TableRow row) => Assert.Single(Assert.IsType<SectionNode>(row.Cells["value"]).Children.OfType<FieldNode>());
}
