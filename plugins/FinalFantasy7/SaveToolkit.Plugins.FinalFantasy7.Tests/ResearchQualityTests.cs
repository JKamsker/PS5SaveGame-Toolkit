using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class ResearchQualityTests
{
    [Theory]
    [InlineData("PPSA01911", 161)]
    [InlineData("PPSA08664", 230)]
    [InlineData("PPSA18936", 230)]
    public void NamedEnemyRecordsUseSourceIndicesAndCorrectCounterWidthsInEveryPrimarySample(string title, int count)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(file => !file.Directory.IsBackup &&
            schema.TryGetRole(file.FileName, out var common) && !common))
        {
            var bytes = file.ReadAllBytes();
            using var document = new FinalFantasy7Plugin().Open(file);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (var pageOffset = 0; pageOffset < count; pageOffset += 200)
            {
                var page = ((IQueryableDocument)document).Query("enemy-intel", new() { Offset = pageOffset, Limit = 200 });
                Assert.Equal(count, page.TotalCount);
                foreach (var row in page.Rows)
                {
                    Assert.True(identities.Add(row.Key));
                    var index = int.Parse(((TextNode)row.Cells["index"]).Text, CultureInfo.InvariantCulture);
                    var offset = (schema.IsRemake ? 0x42FAC : 0x450D0) + index * (schema.IsRemake ? 8 : 16);
                    Assert.Equal($"0x{offset:X}", ((TextNode)row.Cells["address"]).Text);
                    Assert.Equal(((bytes[offset] >> 1) & 1).ToString(), ((TextNode)row.Cells["assessed"]).Text);
                    var defeated = schema.IsRemake ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4))
                        : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 4));
                    Assert.Equal(defeated.ToString(CultureInfo.InvariantCulture), ((TextNode)row.Cells["defeated"]).Text);
                    Assert.Contains("Inferred", ((TextNode)row.Cells["source"]).Text);
                }
            }
            Assert.Equal(count, identities.Count);
            var tabs = (TabsNode)document.BuildView();
            var summary = tabs.Tabs.Single(tab => tab.Title == "Progress").Children.OfType<SectionNode>().SingleOrDefault();
            if (!schema.IsRemake)
            {
                Assert.NotNull(summary);
                Assert.Equal(6, summary.Children.OfType<FieldNode>().Count());
                Assert.All(summary.Children.OfType<FieldNode>(), field =>
                {
                    Assert.True(field.Spec.ReadOnly);
                    Assert.Equal(Confidence.Inferred, field.Confidence);
                    Assert.Contains("MIT", field.Description);
                });
            }
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void CraftingAndMinigameCandidatesPageAllDistinctBitsAndMatchEveryPrimarySample(string title)
    {
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(file => !file.Directory.IsBackup &&
            Ff7Schema.ForTitle(title)!.TryGetRole(file.FileName, out var common) && !common))
        {
            var bytes = file.ReadAllBytes();
            using var document = new FinalFantasy7Plugin().Open(file);
            var queryable = (IQueryableDocument)document;
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var domains = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var offset = 0; offset < 388; offset += 200)
            {
                var page = queryable.Query("research-progress", new() { Offset = offset, Limit = 200 });
                Assert.Equal(388, page.TotalCount);
                foreach (var row in page.Rows)
                {
                    Assert.True(identities.Add(row.Key));
                    var parts = row.Key.Split('/');
                    var byteOffset = Convert.ToInt32(parts[1], 16);
                    var bit = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    Assert.Equal(((bytes[byteOffset] >> bit) & 1).ToString(), ((TextNode)row.Cells["bit"]).Text);
                    var raw = Assert.IsType<FieldNode>(row.Cells["raw"]);
                    Assert.True(raw.Spec.ReadOnly);
                    Assert.Equal(Confidence.Inferred, raw.Confidence);
                    Assert.Equal(bytes[byteOffset], raw.Value.AsInt32());
                    var module = ((TextNode)row.Cells["module"]).Text;
                    domains[module] = domains.GetValueOrDefault(module) + 1;
                    Assert.Contains("MIT", ((TextNode)row.Cells["source"]).Text);
                }
            }
            Assert.Equal(388, identities.Count);
            Assert.Equal(220, domains["Item Transmuter"]);
            Assert.Equal(168, domains["Mini-Games"]);
            Assert.Equal(220, queryable.Query("research-progress", new() { Filters = new Dictionary<string, string> { ["module"] = "Item Transmuter" } }).FilteredCount);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void DuplicateOwnedDetailInstancesStayVisibleAsAmbiguousAndAreNeverRepaired()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var bytes = file.ReadAllBytes();
        const int detailOffset = 0xBFA68;
        var instance = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(detailOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(detailOffset + 0x20), instance);
        using var document = new FinalFantasy7Plugin().Open(MemorySaveSet.SingleFile(file.FileName, bytes, "PPSA08664"));
        var rows = ((IQueryableDocument)document).Query("owned-materia", new() { Text = $"0x{instance:X8}" }).Rows;
        Assert.NotEmpty(rows);
        Assert.Contains(rows, row => ((TextNode)row.Cells["detail"]).Text == "ambiguous" &&
            ((TextNode)row.Cells["owner"]).Text == "Ambiguous detail instance");
        var graph = (GraphNode)document.ExpandNode("spatial/sockets/0")[0];
        Assert.DoesNotContain(graph.Items, item => item.Label.Contains("Ambiguous", StringComparison.Ordinal) && item.State != GraphState.Unknown);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
