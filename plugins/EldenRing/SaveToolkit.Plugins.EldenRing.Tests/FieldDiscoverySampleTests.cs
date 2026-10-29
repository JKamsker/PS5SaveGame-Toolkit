using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class FieldDiscoverySampleTests
{
    private static readonly string[] Attributes =
        ["vigor", "mind", "endurance", "strength", "dexterity", "intelligence", "faith", "arcane"];

    [Fact]
    public void PrimaryAndBackupHaveNoDecodedDifferencesButAreOnlyOneSnapshot()
    {
        var files = SampleSaves.Load("PPSA03169").AllDiskFiles.Where(file => file.FileName == "memory.dat").ToArray();
        Assert.Equal(2, files.Length);
        Assert.Single(files.Select(file => file.Sha256).Distinct(StringComparer.Ordinal));
        var decoder = new HookSaveDecoder(new EldenRingEvidenceDecoder());
        var primary = decoder.Decode(Assert.Single(files, file => !file.Directory.IsBackup), new());
        var backup = decoder.Decode(Assert.Single(files, file => file.Directory.IsBackup), new());
        Assert.False(primary.Truncated);
        Assert.False(backup.Truncated);
        Assert.Equal(primary.Values, backup.Values);
        Assert.Empty(SemanticDiffer.Compare(primary, backup).Changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySuppliedRecordHasConsistentStatsNamedEquipmentAndNoLarvalTearRows(bool backup)
    {
        var set = SampleSaves.Load("PPSA03169");
        var files = set.AllDiskFiles.Where(file => file.FileName == "memory.dat" && file.Directory.IsBackup == backup).ToArray();
        Assert.Single(files); // This corpus has one generation, not a temporal comparison.
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            var parsed = EldenRingParser.Parse(bytes);
            var slot = Assert.Single(parsed.Slots, slot => slot.Active);
            Assert.Equal(0, slot.Index);
            Assert.Equal(0xA2FF, slot.PlayerOffset);
            var character = SemanticReader.Read(bytes, slot);
            var decoder = new HookSaveDecoder(new EldenRingEvidenceDecoder());
            var tree = decoder.Decode(file, new());
            Assert.False(tree.Truncated);
            uint[] expected = [13, 13, 11, 11, 16, 10, 8, 9];
            for (var index = 0; index < Attributes.Length; index++)
                Assert.Contains(tree.Values, value => value.Path == "slots/0/attributes/" + Attributes[index]
                    && value.Value == expected[index].ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(91U, expected.Aggregate(0U, (sum, value) => sum + value));
            Assert.Contains(tree.Values, value => value.Path == "slots/0/level" && value.Value == "12");
            Assert.Contains(tree.Values, value => value.Path == "slots/0/profile-level" && value.Value == "12");
            Assert.DoesNotContain(character.Containers.SelectMany(container => container.Entries), entry => entry.ItemId == 0x40001FF9);

            var plugin = new EldenRingPlugin();
            var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169");
            Assert.Equal("Larval Tear", references.CatalogForHost(ReferenceBrowser.CatalogId)!.Find("0x40001FF9")!.Name);
            using var document = plugin.Open(file, references);
            var nodes = document.ExpandNode("characters/0");
            var links = EquipmentReader.Read(bytes, character);
            Assert.Equal(34, links.Count);
            Assert.Equal(17, links.Count(link => link.Status.StartsWith("Representations agree", StringComparison.Ordinal)));
            var table = Assert.Single(nodes.OfType<TableNode>(), table => table.Title == "Existing equipment relationships (read-only)");
            foreach (var link in links.Where(link => link.Entry is not null))
            {
                var row = Assert.Single(table.Rows, row => row.Key.EndsWith("/" + link.Key, StringComparison.Ordinal));
                var item = Assert.IsType<FieldNode>(row.Cells["item"]);
                Assert.Equal(Confidence.Inferred, item.Confidence);
                Assert.True(item.Spec.ReadOnly);
                Assert.NotEqual(link.Entry!.RawId, item.Label); // Every observed identity has a catalog name.
                Assert.Contains(tree.Values, value => value.Path == "slots/0/equipment-links/" + link.Key + "/identity"
                    && value.Value == link.Entry.RawId);
            }
            Assert.Equal(504U, document.GetValue("slots/0/vitals/hp/current").AsUInt32());
            Assert.Equal(504U, document.GetValue("slots/0/vitals/hp/effective-max").AsUInt32());
            Assert.Equal(476U, document.GetValue("slots/0/vitals/hp/base-max").AsUInt32());
            Assert.Equal(88U, document.GetValue("slots/0/vitals/fp/base-max").AsUInt32());
            Assert.Equal(97U, document.GetValue("slots/0/vitals/stamina/base-max").AsUInt32());
            WriteTestSupport.EqualBytes(bytes, document);
            Assert.True(bytes.AsSpan().SequenceEqual(file.ReadAllBytes()));
        }
    }
}
