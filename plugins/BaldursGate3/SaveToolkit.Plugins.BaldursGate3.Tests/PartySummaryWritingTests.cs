using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class PartySummaryWritingTests
{
    [Theory]
    [InlineData("Level", "level")]
    [InlineData(PartySummaryEdits.CurrentXp, "currentXp")]
    [InlineData(PartySummaryEdits.TotalXp, "totalXp")]
    public void SampleSummaryEditsPersistOnlyInIntendedTokensAndSupportSessionUndoRedo(string property, string column)
    {
        var file = Assert.IsType<DiskSaveFile>(SampleSaves.Load("PPSA18463").AllDiskFiles
            .First(file => file.FileName == "AutoSave_0.lsv" && !file.Directory.IsBackup));
        var original = file.ReadAllBytes();
        var plugin = new BaldursGate3Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("bg3-summary", file, plugin, document);
        var fields = Rows(session.BuildView()).Select(row => Assert.IsType<FieldNode>(row.Cells[column])).ToArray();
        Assert.Equal(3, fields.Length);
        foreach (var field in fields)
        {
            Assert.False(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Contains("Not validated on console", field.Description, StringComparison.Ordinal);
            Assert.Contains("game may reject", field.Warning, StringComparison.Ordinal);
            var spec = Assert.IsType<IntegerSpec>(field.Spec);
            foreach (var invalid in new[] { spec.Min - 1, spec.Max + 1 })
            {
                Assert.False(session.ApplyEdits([new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement(invalid.ToString()))]).Ok);
                Assert.False(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of(invalid))]).Succeeded);
            }
        }
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        var next = fields.ToDictionary(field => field.ValueId,
            field => property == "Level" ? field.Value.AsInt64() + 1 : field.Value.AsInt64() == 0 ? 1L : field.Value.AsInt64() - 1);
        Assert.True(session.ApplyEdits(fields.Select(field =>
            new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement(next[field.ValueId]))).ToArray()).Ok);
        var edited = Assert.Single(session.Serialize()).Content.ToArray();
        AssertOnlySummaryChanged(original, edited, fields.Select(field => field.ValueId).ToArray());
        using var reopened = plugin.Open(MemorySaveSet.SingleFile("edited.lsv", edited, "PPSA18463"));
        _ = reopened.BuildView();
        foreach (var field in fields) Assert.Equal(next[field.ValueId], reopened.GetValue(field.ValueId).AsInt64());
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo); // the whole multi-row edit is one history step
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(session.Serialize()).Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void LengthChangingEditsPreserveUnknownJsonSpellingAndAtomicFailureRestoresAllTokens()
    {
        const string json = " {\n\"unknown\": [1e+0090,-0,\"\\u96ea\"], \"Active Party\" : {\"Characters\" : [" +
            "{\"Origin\":\"Generic\",\"Le\\u0076el\":1,\"Experience Points (Current level)\":30,\"Experience Points (Total)\":-0}]}}\r\n";
        var original = PackageFixture.Create(method: 2, payloads: [("SaveInfo.json", Encoding.UTF8.GetBytes(json)), ("unknown.bin", [0xde, 0xad])]);
        using var document = new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("test.lsv", original, "PPSA18463"));
        _ = document.BuildView();
        var edits = new[] { new ValueEdit("party/0/Level", FieldValue.Of(12L)),
            new ValueEdit($"party/0/{PartySummaryEdits.CurrentXp}", FieldValue.Of(0L)) };
        Assert.False(document.ApplyEdits([edits[0], new ValueEdit(edits[1].ValueId, FieldValue.Of(-1L))]).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        var outcome = document.ApplyEdits(edits);
        Assert.True(outcome.Succeeded);
        var output = LspkPackage.Read(Assert.Single(document.Serialize()).Content.ToArray());
        var actual = output.Extract(output.Entries.Single(entry => entry.Name == "SaveInfo.json"));
        var expected = json.Replace("\\u0076el\":1", "\\u0076el\":12", StringComparison.Ordinal)
            .Replace("(Current level)\":30", "(Current level)\":0", StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), actual);
        Assert.True(document.ApplyEdits(outcome.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("{\"Active Party\":{\"Characters\":[{\"Level\":1,\"Level\":2}]}}")]
    [InlineData("{\"Active Party\":{\"Characters\":[{\"Level\":1,\"Le\\u0076el\":2}]}}")]
    [InlineData("{\"Active Party\":{\"Characters\":[{\"Level\":1}]},\"Active Party\":{\"Characters\":[{\"Level\":2}]}}")]
    [InlineData("{\"Active Party\":{\"Characters\":[{\"Level\":1.0,\"Experience Points (Total)\":1e50}]}}")]
    public void AmbiguousOrUnsupportedSummaryRemainsReadOnlyAndByteIdentical(string json)
    {
        var original = PackageFixture.Create(payloads: [("SaveInfo.json", Encoding.UTF8.GetBytes(json))]);
        using var document = new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("test.lsv", original, "PPSA18463"));
        var numbers = Rows(document.BuildView()).SelectMany(row => row.Cells.Values).OfType<FieldNode>();
        Assert.All(numbers, field => Assert.True(field.Spec.ReadOnly));
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData((uint)15, false)]
    [InlineData((uint)16, false)]
    [InlineData((uint)18, true)]
    public void MetadataOnlyPackageHonorsVersionWriteSupport(uint version, bool editable)
    {
        var original = PackageFixture.Create(version: version, payloads: [("SaveInfo.json", "{\"Active Party\":{\"Characters\":[{\"Level\":1}]}}"u8.ToArray())]);
        using var document = new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("test.lsv", original, "PPSA18463"));
        Assert.Equal(editable, document.CanEdit);
        Assert.Equal(!editable, Assert.IsType<FieldNode>(Rows(document.BuildView())[0].Cells["level"]).Spec.ReadOnly);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static IReadOnlyList<TableRow> Rows(ViewNode view) => Assert.Single(Assert.Single(
        Assert.IsType<TabsNode>(view).Tabs, tab => tab.Title == "Party").Children.OfType<TableNode>()).Rows;

    private static void AssertOnlySummaryChanged(byte[] original, byte[] edited, IReadOnlyList<string> ids)
    {
        var before = LspkPackage.Read(original);
        var after = LspkPackage.Read(edited); // validates recomputed digest
        Assert.NotEqual(before.HeaderDigest, after.HeaderDigest);
        foreach (var entry in before.Entries)
        {
            var rewritten = after.Entries.Single(other => other.Name == entry.Name);
            if (entry.Name != "SaveInfo.json")
            {
                Assert.True(original.AsSpan(entry.Offset, entry.StoredSize).SequenceEqual(edited.AsSpan(rewritten.Offset, rewritten.StoredSize)));
                continue;
            }
            var input = before.Extract(entry);
            var output = after.Extract(rewritten);
            var summary = PartySummaryEdits.Create([new DocumentEntry(entry.Name, input, null)])!;
            var expected = ids.Select(id => summary.Find(id)!).ToArray();
            var diff = ByteDiffer.Compare(input, output, options: new ByteDiffOptions { MergeGap = 0, ContextBytes = 0 });
            Assert.True(diff.HasChanges);
            Assert.Equal(input.Length, output.Length);
            Assert.All(diff.Ranges, range => Assert.Contains(expected, number => range.Offset >= number.Offset && range.End <= number.Offset + number.Length));
        }
    }
}
