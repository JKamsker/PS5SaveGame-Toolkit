using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class FactWriteTests
{
    [Fact]
    public void ExistingFactsInEveryPlanePersistWithExactFourBytePatchesAndUndoRedo()
    {
        var file = ProgressionSampleTests.Files()[0];
        var before = CsavContainer.Parse(file.ReadAllBytes());
        var tree = CsavTree.Parse(before);
        var plugin = new Cyberpunk2077Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("facts-write", (DiskSaveFile)file, plugin, document);
        _ = session.BuildView();
        foreach (var node in tree.Nodes.Where(node => node.Name == "FactsTable"))
        {
            var query = new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["table"] = node.Id.ToString(CultureInfo.InvariantCulture) }, Limit = 200 };
            var pageBefore = session.QueryCollection("facts", query);
            if (FactsReader.Read(before.Payload.AsSpan(node.Offset, node.Size)).Count == 0)
            {
                Assert.Empty(pageBefore.Rows);
                continue;
            }
            var row = pageBefore.Rows[0];
            var field = Assert.IsType<FieldNode>(row.Cells["value"]);
            Assert.False(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Unknown, field.Confidence);
            Assert.Contains("Not validated on console", field.Description);
            Assert.Contains("quest stages", field.Warning, StringComparison.OrdinalIgnoreCase);
            var next = field.Value.AsUInt32() == 0 ? 1U : 0U;
            Assert.True(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(next.ToString(CultureInfo.InvariantCulture)))]).Ok);
            var bytes = Assert.Single(session.Serialize()).Content.ToArray();
            var after = CsavContainer.Parse(bytes);
            var countOffset = sizeof(int);
            var count = BinaryData.PackedInt(before.Payload.AsSpan(node.Offset, node.Size), ref countOffset);
            var index = int.Parse(row.Key.Split('/')[2], CultureInfo.InvariantCulture);
            var offset = node.Offset + countOffset + (count + index) * sizeof(uint);
            var expected = before.Payload.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset), next);
            Assert.Equal(expected, after.Payload);
            var diff = ByteDiffer.Compare(before.Payload, after.Payload);
            Assert.InRange(diff.ChangedBytes, 1, sizeof(uint));
            Assert.All(diff.Ranges, range => Assert.InRange(range.Offset, offset, offset + sizeof(uint) - 1));
            using var reopened = ProgressionSampleTests.Open(bytes);
            var page = Assert.IsType<CyberpunkDocument>(reopened).Query("facts", query);
            Assert.Equal(next, Assert.IsType<FieldNode>(page.Rows.Single(candidate => candidate.Key == row.Key).Cells["value"]).Value.AsUInt32());
            var catalog = session.QueryCollection("fact-catalog", query);
            var catalogRow = catalog.Rows.Single(candidate => candidate.Key == $"fact-catalog/saved/{node.Id}/{index}");
            Assert.Equal(next.ToString(CultureInfo.InvariantCulture), Assert.IsType<SectionNode>(catalogRow.Cells["value"]).Title);
            Assert.Equal(next, Assert.IsType<FieldNode>(session.QueryCollection("facts", query).Rows.Single(candidate => candidate.Key == row.Key).Cells["value"]).Value.AsUInt32());
            Assert.True(session.Undo().Ok);
            Assert.Equal(before.Original, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(bytes, Assert.Single(session.Serialize()).Content.ToArray());
            Assert.True(session.Undo().Ok);
        }
    }

    [Fact]
    public void InvalidFactValuesAndAbsentFactsAreRejectedWithoutChangingMoney()
    {
        var file = ProgressionSampleTests.Files()[0];
        using var document = new Cyberpunk2077Plugin().Open(file);
        _ = CyberpunkSampleTests.ExpandAll(document);
        var field = Assert.IsType<FieldNode>(Assert.IsType<CyberpunkDocument>(document)
            .Query("facts", new QueryableCollectionQuery()).Rows[0].Cells["value"]);
        var maximum = Assert.IsType<IntegerSpec>(field.Spec).Max;
        foreach (var value in new[] { FieldValue.Of(-1L), FieldValue.Of(maximum + 1), FieldValue.Of("invalid") })
        {
            Assert.False(document.ApplyEdits([new("inventory/39/quantity", FieldValue.Of(99L)), new(field.ValueId, value)]).Succeeded);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
        Assert.False(document.ApplyEdits([new("fact-catalog/absent/123", FieldValue.Of(1L))]).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }
}
