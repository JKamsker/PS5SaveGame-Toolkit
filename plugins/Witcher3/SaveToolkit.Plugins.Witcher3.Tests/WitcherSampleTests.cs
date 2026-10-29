using System.Diagnostics;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherSampleTests(ITestOutputHelper output)
{
    [Fact]
    public void EverySnapshotAndBackupMatchesKnownSav3Values()
    {
        var expected = new Dictionary<string, (int Expanded, int Variables, int Names)>(StringComparer.Ordinal)
        {
            ["CheckPt.53db8.7e73c800.313c5b3.sav"] = (653545, 16082, 1980),
            ["CheckPt.8559a.7e73c800.317a3e7.sav"] = (702856, 17399, 2053),
            ["CheckPt.8559a.7e73c800.31c45eb.sav"] = (703834, 17375, 2079),
            ["Manual.8559a.7e73c800.31c94f5.sav"] = (724174, 18445, 2106),
        };
        var plugin = new Witcher3Plugin();
        var files = SampleFiles();
        Assert.Equal(expected.Keys.Order(), files.Select(file => file.FileName).Distinct().Order());
        foreach (var file in files)
        {
            var known = expected[file.FileName];
            var container = WitcherContainer.Parse(file.ReadAllBytes());
            var directory = Sav3Directory.Parse(container);
            var tree = Sav3Tree.Parse(container.Payload, directory);
            Assert.Equal(3084, container.HeaderSize);
            Assert.Single(container.Chunks);
            Assert.Equal(known.Expanded, container.Payload.Length);
            Assert.Equal(known.Variables, directory.Entries.Count);
            Assert.Equal(known.Names, directory.Names.Count);
            Assert.Equal("Entity", directory.Names[0]);
            Assert.Contains("Player", directory.Names);
            Assert.Contains("Crowns", directory.Names);
            Assert.True(tree.Nodes.Count > directory.Entries.Count);
            var rawPath = Assert.Single(tree.Nodes, node => node.Offset == 20);
            Assert.Equal("path", rawPath.Name);
            Assert.Equal("game\\witcher3.redgame", ScalarReader.Read(rawPath, container.Payload, directory.Names)!.Value.AsString());
            Assert.Contains(tree.Nodes, node => node.Tag == "BLCK");
            Assert.Contains(tree.Nodes, node => node.Tag == "PORP");
            Assert.Contains(tree.Nodes, node => node.Tag == "AVAL");
            var timer = Stopwatch.StartNew();
            using var document = plugin.Open(file);
            Assert.IsType<TabsNode>(document.BuildView());
            timer.Stop();
            output.WriteLine($"{file.FileName}: open + initial view {timer.Elapsed.TotalMilliseconds:F1} ms, {tree.Nodes.Count} explorer nodes");
            Assert.True(document.CanEdit);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void EntireLazyExplorerIsBoundedAndEveryValueIsReadable()
    {
        var plugin = new Witcher3Plugin();
        foreach (var file in SampleFiles().DistinctBy(file => file.FileName))
        {
            using var document = plugin.Open(file);
            var stats = Explore(document);
            Assert.True(stats.Fields > 5000);
            Assert.True(stats.Sections > 16000);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void RejectsEditsAndDoesNotClaimSettingsOrRequiredContent()
    {
        var plugin = new Witcher3Plugin();
        foreach (var file in SampleSaves.Load("PPSA04018").AllDiskFiles.Where(file => !file.FileName.EndsWith(".sav", StringComparison.Ordinal)))
        {
            Assert.False(plugin.Probe(file).IsMatch);
        }

        var sample = SampleFiles()[0];
        using var document = plugin.Open(sample);
        _ = document.BuildView();
        Assert.False(document.ApplyEdits([new ValueEdit("header/0", FieldValue.Of(1L))]).Succeeded);
        Assert.Equal(sample.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("payload", -1, 4));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("node/-1"));
    }

    private static (int Fields, int Sections) Explore(ISaveDocument document)
    {
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<ViewNode>();
        var fields = 0;
        pending.Enqueue(document.BuildView());
        while (pending.TryDequeue(out var node))
        {
            if (node is FieldNode field)
            {
                fields++;
                Assert.True(field.Spec.ReadOnly || field.ValueId.StartsWith("player/", StringComparison.Ordinal) ||
                    field.ValueId.StartsWith("inventory/", StringComparison.Ordinal) ||
                    field.ValueId.StartsWith("abilities/skills/", StringComparison.Ordinal) ||
                    field.ValueId.StartsWith("gwent/collection/", StringComparison.Ordinal));
                Assert.Equal(field.Value, document.GetValue(field.ValueId));
            }

            if (node is TableNode table)
            {
                Assert.True(table.Rows.Count <= 128);
            }

            IEnumerable<ViewNode> children = node switch
            {
                TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children),
                SectionNode { LazyId: { } id } when expanded.Add(id) => document.ExpandNode(id),
                SectionNode { LazyId: null } section => section.Children,
                TableNode rows => rows.Rows.SelectMany(row => row.Cells.Values),
                _ => [],
            };
            var batch = children.ToArray();
            // Rich tables now include an image cell; retain the row bound and account for declared columns.
            Assert.True(batch.Length <= (node is TableNode richTable ? 128 * richTable.Columns.Count : 256));
            foreach (var child in batch)
            {
                pending.Enqueue(child);
            }
        }

        return (fields, expanded.Count);
    }

    private static ISaveFile[] SampleFiles() => SampleSaves.Load("PPSA04018").AllDiskFiles
        .Where(file => file.FileName.EndsWith(".sav", StringComparison.Ordinal)).Cast<ISaveFile>().ToArray();
}
