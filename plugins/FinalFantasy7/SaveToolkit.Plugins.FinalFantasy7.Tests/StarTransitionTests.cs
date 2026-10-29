using System.Text.Json;
using System.Globalization;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class StarTransitionTests
{
    [Fact]
    public void ReapplyingStoredStarsDoesNotNormalizeAboveThresholdAp()
    {
        const string title = "PPSA08664";
        const string name = "ff7rebirth000";
        var schema = Ff7Schema.ForTitle(title)!;
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == name);
        var plugin = new FinalFantasy7Plugin();
        var original = file.ReadAllBytes();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("same-stars", file, plugin, document);
        _ = session.BuildView();

        var row = session.QueryCollection("materia", new() { Limit = 128 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows.First(row =>
        {
            if (row.Cells["quantity"] is not FieldNode { Spec.ReadOnly: false } apField ||
                row.Cells["level"] is not FieldNode levelField)
            {
                return false;
            }

            var id = uint.Parse(((FieldNode)row.Cells["id"]).Value.AsString(), CultureInfo.InvariantCulture);
            var curve = EmbeddedCatalogs.Records(schema, "ff7r-materia").Single(record => record.Key == id.ToString(CultureInfo.InvariantCulture))
                .Attributes["apThresholds"].Split(',').Select(value => uint.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            var level = levelField.Value.AsInt32();
            return level >= 1 && level <= curve.Length && apField.Value.AsUInt32() > curve[level - 1];
        });
        var level = ((FieldNode)row.Cells["level"]).Value.AsInt32();

        var result = session.ApplyEdits([new(((FieldNode)row.Cells["level"]).ValueId,
            JsonSerializer.SerializeToElement(level))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA01911", "ff7remake000")]
    [InlineData("PPSA01911", "ff7remakeplus000")]
    [InlineData("PPSA08664", "ff7rebirth000")]
    public void AllTiersAndThresholdNeighborsReopenAndUndoExactly(string title, string name)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == name);
        var plugin = new FinalFantasy7Plugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("stars", file, plugin, document);
        _ = session.BuildView();
        var rows = session.QueryCollection("materia", new() { Limit = 128 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows;
        var row = rows.First(row => row.Cells["quantity"] is FieldNode { Spec.ReadOnly: false });
        var apField = (FieldNode)row.Cells["quantity"];
        var levelField = (FieldNode)row.Cells["level"];
        var id = uint.Parse(((FieldNode)row.Cells["id"]).Value.AsString(), CultureInfo.InvariantCulture);
        var curve = EmbeddedCatalogs.Records(schema, "ff7r-materia").Single(record => record.Key == id.ToString())
            .Attributes["apThresholds"].Split(',').Select(uint.Parse).ToArray();
        var slot = int.Parse(row.Key.Split('/')[1]);
        var apOffset = (schema.IsRemake ? 0x1848 : 0xBFA70) + slot * 32;
        var starOffset = (schema.IsRemake ? 0x1840 : 0xBFA78) + slot * 32;
        var original = file.ReadAllBytes();
        var points = curve.SelectMany(value => new[] { value == 0 ? 0 : value - 1, value, value + 1 }).Distinct();
        foreach (var ap in points)
        {
            Assert.True(session.ApplyEdits([new(apField.ValueId, JsonSerializer.SerializeToElement(ap))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
            var edited = Assert.Single(document.Serialize()).Content.ToArray();
            var diff = ByteDiffer.Compare(original, edited, options: new() { MergeGap = 0 });
            Assert.All(diff.Ranges, range => Assert.True(
                range.Offset >= apOffset && range.End <= apOffset + 4 || range.Offset == starOffset && range.Length == 1));
            using var reopened = plugin.Open(MemorySaveSet.SingleFile(name, edited, title));
            _ = reopened.ExpandNode("materia/0");
            Assert.Equal(ap, reopened.GetValue(apField.ValueId).AsUInt32());
            Assert.Equal(curve.Count(threshold => ap >= threshold), reopened.GetValue(levelField.ValueId).AsInt32());
            Assert.True(session.Undo().Ok);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Undo().Ok);
        }
        for (var level = 1; level <= curve.Length; level++)
        {
            Assert.True(session.ApplyEdits([new(levelField.ValueId, JsonSerializer.SerializeToElement(level))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
            var edited = Assert.Single(document.Serialize()).Content.ToArray();
            var diff = ByteDiffer.Compare(original, edited, options: new() { MergeGap = 0 });
            Assert.All(diff.Ranges, range => Assert.True(
                range.Offset >= apOffset && range.End <= apOffset + 4 || range.Offset == starOffset && range.Length == 1));
            using var reopened = plugin.Open(MemorySaveSet.SingleFile(name, edited, title));
            _ = reopened.ExpandNode("materia/0");
            Assert.Equal(curve[level - 1], reopened.GetValue(apField.ValueId).AsUInt32());
            Assert.Equal(level, reopened.GetValue(levelField.ValueId).AsInt32());
            Assert.True(session.Undo().Ok);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Undo().Ok);
        }
        Assert.False(document.ApplyEdits([new(levelField.ValueId, FieldValue.Of(0L))]).Succeeded);
        Assert.False(document.ApplyEdits([new(levelField.ValueId, FieldValue.Of((long)curve.Length + 1))]).Succeeded);
        if (curve.Length > 1)
            Assert.False(document.ApplyEdits([new(apField.ValueId, FieldValue.Of((long)curve[^1])),
                new(levelField.ValueId, FieldValue.Of(1L))]).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
