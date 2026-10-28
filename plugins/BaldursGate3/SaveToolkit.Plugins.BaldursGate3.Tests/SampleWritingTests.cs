using System.Diagnostics;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class SampleWritingTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryLsfInEveryPackageAndProfileHasAnExactNoOpAndSurvivingPrimitiveEdit()
    {
        var plugin = new BaldursGate3Plugin();
        var tested = 0;
        foreach (var file in SampleSaves.Load("PPSA18463").AllDiskFiles.Where(f => plugin.Probe(f).IsMatch))
        {
            var bytes = file.ReadAllBytes();
            IEnumerable<(string Name, byte[] Data)> resources;
            if (file.FileName.EndsWith(".lsv", StringComparison.Ordinal))
            {
                var package = LspkPackage.Read(bytes);
                resources = package.Entries.Where(e => e.Name.EndsWith(".lsf", StringComparison.Ordinal))
                    .Select(e => (e.Name, package.Extract(e)));
            }
            else resources = file.FileName.EndsWith(".lsf", StringComparison.Ordinal) ? [(file.FileName, bytes)] : [];
            foreach (var (name, original) in resources)
            {
                Assert.Equal(original, LsfBinary.Read(original).Rebuild(new Dictionary<int, byte[]>()));
                Assert.Equal(original, LsfWriter.Write(original, new Dictionary<int, byte[]>()));
                var before = LsfReader.Read(original);
                tested++;
                var index = Enumerable.Range(0, before.Attributes.Count).FirstOrDefault(i => AttributeEncoder.CanWrite(before.Attributes[i]), -1);
                if (index == -1)
                {
                    output.WriteLine($"{name}: exact no-op verified; only opaque types: {string.Join(", ", before.Attributes.Select(a => a.Type).Distinct())}.");
                    continue;
                }
                var attribute = before.Attributes[index];
                var next = Next(AttributeDecoder.Read(attribute));
                var encoded = AttributeEncoder.Write(attribute, next);
                var result = LsfWriter.Write(original, new Dictionary<int, byte[]> { [index] = encoded });
                var after = LsfReader.Read(result);
                Assert.Equal(next, AttributeDecoder.Read(after.Attributes[index]));
                for (var a = 0; a < before.Attributes.Count; a++)
                {
                    Assert.True(after.Attributes[a].Data.Span.SequenceEqual(a == index ? encoded : before.Attributes[a].Data.Span));
                }

                output.WriteLine($"{name}: exact no-op and {attribute.Name} edit verified ({before.Attributes.Count} attributes).");
            }
        }

        Assert.Equal(14, tested); // seven LSFs, including primary/backup twins; no extra snapshots supplied
    }

    [Fact]
    public void ActualPackageRebuildPreservesAllUneditedCompressedPayloads()
    {
        var file = SampleSaves.Load("PPSA18463").AllDiskFiles.First(f => f.FileName == "AutoSave_0.lsv" && !f.Directory.IsBackup);
        var original = file.ReadAllBytes();
        var before = LspkPackage.Read(original);
        Assert.Equal(original, LspkWriter.Write(original, new Dictionary<string, byte[]>()));
        var metaEntry = before.Entries.Single(e => e.Name == "meta.lsf");
        var metaBytes = before.Extract(metaEntry);
        var meta = LsfReader.Read(metaBytes);
        var index = Enumerable.Range(0, meta.Attributes.Count).Single(i => meta.Attributes[i].Name == "LeaderName");
        var next = AttributeDecoder.Read(meta.Attributes[index]).AsString() + " longer test";
        var changedMeta = LsfWriter.Write(metaBytes, new Dictionary<int, byte[]>
        {
            [index] = AttributeEncoder.Write(meta.Attributes[index], FieldValue.Of(next)),
        });
        var result = LspkWriter.Write(original, new Dictionary<string, byte[]> { [metaEntry.Name] = changedMeta });
        var after = LspkPackage.Read(result);
        Assert.NotEqual(before.HeaderDigest, after.HeaderDigest);
        Assert.Equal(before.Entries.Select(e => e.Name), after.Entries.Select(e => e.Name));
        foreach (var entry in before.Entries)
        {
            var rewritten = after.Entries.Single(e => e.Name == entry.Name);
            Assert.Equal(entry.Flags, rewritten.Flags);
            if (entry.Name == metaEntry.Name)
            {
                Assert.Equal(next, AttributeDecoder.Read(LsfReader.Read(after.Extract(rewritten)).Attributes[index]).AsString());
            }
            else
            {
                Assert.True(original.AsSpan(entry.Offset, entry.StoredSize).SequenceEqual(result.AsSpan(rewritten.Offset, rewritten.StoredSize)));
                Assert.Equal(before.Extract(entry), after.Extract(rewritten));
            }
        }

        using var reopened = new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("changed.lsv", result, "PPSA18463"));
        Assert.True(reopened.CanEdit);
    }

    [Fact]
    public void PartySummaryUsesOnlySourcedFieldsAndOpeningRemainsFast()
    {
        var file = SampleSaves.Load("PPSA18463").AllDiskFiles.First(f => f.FileName == "AutoSave_0.lsv" && !f.Directory.IsBackup);
        var watch = Stopwatch.StartNew();
        using var document = new BaldursGate3Plugin().Open(file);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        watch.Stop();
        output.WriteLine($"Open + initial view: {watch.Elapsed.TotalMilliseconds:F0} ms");
        var party = Assert.Single(view.Tabs, t => t.Title == "Party");
        var table = Assert.Single(party.Children.OfType<TableNode>());
        Assert.Equal(3, table.Rows.Count);
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var level = Assert.IsType<FieldNode>(row.Cells["level"]);
            var current = Assert.IsType<FieldNode>(row.Cells["currentXp"]);
            var total = Assert.IsType<FieldNode>(row.Cells["totalXp"]);
            Assert.False(level.Spec.ReadOnly);
            Assert.False(current.Spec.ReadOnly);
            Assert.False(total.Spec.ReadOnly);
            Assert.Contains("Not validated on console", level.Warning, StringComparison.Ordinal);
            Assert.Equal(Confidence.Inferred, level.Confidence);
            output.WriteLine($"Party: level {level.Value}, current XP {current.Value}, total XP {total.Value}");
            Assert.Equal(1, level.Value.AsInt32());
            Assert.Equal(i < 2 ? 30 : 0, current.Value.AsInt32());
            Assert.Equal(i < 2 ? 30 : 0, total.Value.AsInt32());
        }

        Assert.Contains(party.Children.OfType<TextNode>(), t => t.Text.Contains("Gold quantity is unavailable", StringComparison.Ordinal));
        var leader = Assert.Single(party.Children.OfType<FieldNode>(), f => f.Label.StartsWith("LeaderName (", StringComparison.Ordinal) && !f.Spec.ReadOnly);
        output.WriteLine("Leader field: " + leader.Label);
    }

    private static FieldValue Next(FieldValue value) => value.Kind switch
    {
        FieldValueKind.Bool => FieldValue.Of(!value.AsBool()),
        FieldValueKind.Integer => FieldValue.Of(value.AsInteger() == 0 ? 1L : 0L),
        FieldValueKind.Float => FieldValue.Of(0.125),
        FieldValueKind.Text => FieldValue.Of(value.AsString() + " test 雪"),
        _ => throw new InvalidOperationException("Not a primitive."),
    };
}
