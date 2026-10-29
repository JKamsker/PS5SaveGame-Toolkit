using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class AbilityDiscoveryTests
{
    private const string CreationStats = "game.character_creation.v1.CharacterCreationStatsComponent";
    private const string LevelUpAbilities = "game.character_creation.v0.LevelUpComponentAbilities";
    private const int CreationRecordBytes = 88;
    private const int AllocationBeginOffset = 72;
    private const int AllocationEndOffset = 80;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothCopiesHavePointBuyShapedAllocationsButNoReliablePartyOwnerJoin(bool backup)
    {
        var file = Assert.Single(SampleSaves.Load("PPSA18463").AllDiskFiles,
            file => file.FileName == "AutoSave_0.lsv" && file.Directory.IsBackup == backup);
        var source = SampleLiveNewAgeTests.Source(file.ReadAllBytes());
        var rows = source.Access.Rows(CreationStats, CreationRecordBytes);
        Assert.Equal(2, rows.Count);
        var allocations = rows.Select(row => Allocation(source.Access, row.Offset)).ToArray();
        Assert.Equal([0, 0, 5, 6, 4, 2, 7], allocations[0]);
        Assert.Equal([0, 7, 5, 6, 2, 4, 0], allocations[1]);
        // Hypothesis: sentinel + six increases over 8, not the six effective scores.
        // The cost curve is from bg3.wiki/Abilities, accessed 2026-10-29; facts only.
        int[] pointCosts = [0, 1, 2, 3, 4, 5, 7, 9];
        Assert.All(allocations, allocation => Assert.Equal(27, allocation.Skip(1).Sum(value => pointCosts[value])));
        Assert.Equal([8, 13, 14, 12, 10, 15], allocations[0].Skip(1).Select(value => value + 8));
        Assert.Equal([15, 13, 14, 10, 12, 8], allocations[1].Skip(1).Select(value => value + 8));
        Assert.Equal([8L, 13L, 15L, 12L, 10L, 17L], Effective(source, 2));
        Assert.Equal([17L, 13L, 15L, 10L, 12L, 8L], Effective(source, 5));
        Assert.Equal([246, 247], rows.Select(row => row.EntityRow!.Value));
        var identity = Assert.IsType<LsmfIdentityGraph>(source.Identity);
        Assert.Equal(247, identity.Party.Single(member => member.StatsRow == 3).StateEntityRow);
        Assert.Equal(249, identity.Party.Single(member => member.StatsRow == 5).StateEntityRow);
        Assert.Equal([10L, 10L, 3L, 2L, 1L, 1L], Effective(source, 3));
        // Creation row 1's owner points at stats row 3, not the numerical match at row 5.
        // Do not regress to treating the raw owner row or matching score shape as identity.
        var levels = source.Access.Rows(LevelUpAbilities, 16);
        Assert.Equal(4, levels.Count);
        Assert.All(levels, level =>
        {
            var values = Array(source.Access, level.Offset, level.Offset + 8);
            Assert.Equal(7, values.Length);
            Assert.All(values, value => Assert.Equal(0, value));
        });
        using var document = new BaldursGate3Plugin().Open(file);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryEmbeddedResourceIsSurveyedAndUnsupportedLevelNewAgeIsExplicit(bool backup)
    {
        var file = Assert.Single(SampleSaves.Load("PPSA18463").AllDiskFiles,
            file => file.FileName == "AutoSave_0.lsv" && file.Directory.IsBackup == backup);
        var package = LspkPackage.Read(file.ReadAllBytes());
        var resources = package.Entries.Where(entry => entry.Name.EndsWith(".lsf", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, resources.Length);
        foreach (var entry in resources)
        {
            var bytes = package.Extract(entry);
            var resource = LsfReader.Read(bytes);
            Assert.DoesNotContain(resource.Attributes, attribute => attribute.Name is "Abilities" or "BaseAbilities" or "Gold" or "PartyGold");
            var gold = resource.Nodes.Where(node => node.Name == "Item" && node.Attributes.Any(index =>
                resource.Attributes[index].Name == "Stats" && AttributeDecoder.Read(resource.Attributes[index]).AsString()
                    .StartsWith("OBJ_Gold", StringComparison.Ordinal))).ToArray();
            Assert.Equal(entry.Name switch { "Globals.lsf" => 4, "LevelCache/TUT_Avernus_C.lsf" => 7, _ => 0 }, gold.Length);
            Assert.All(gold, node => Assert.DoesNotContain(node.Attributes.Select(index => resource.Attributes[index]),
                attribute => attribute.Name is "Amount" or "Quantity" or "StackAmount" or "Owner"));
            Assert.Equal(bytes, LsfBinary.Read(bytes).Rebuild(new Dictionary<int, byte[]>()));
            if (entry.Name == "LevelCache/TUT_Avernus_C.lsf")
            {
                var blob = Assert.Single(resource.Attributes, attribute => attribute.Name == "NewAge");
                Assert.Contains("Unaligned LSMF directory", Assert.Throws<SaveFormatException>(() => LsmfReader.Read(blob.Data)).Message, StringComparison.Ordinal);
            }
            if (entry.Name == "LevelCache/SYS_CC_I.lsf")
            {
                var blob = Assert.Single(resource.Attributes, attribute => attribute.Name == "NewAge");
                var model = LsmfReader.Read(blob.Data);
                Assert.Null(model.Component(CreationStats));
                Assert.Null(model.Component(LevelUpAbilities));
            }
        }
    }

    private static long[] Effective(LsmfLiveSource source, int row) => source.Numbers
        .Where(number => number.Family == "Stats" && number.Row == row).Select(number => number.Original).ToArray();

    private static int[] Allocation(LsmfAccess access, int record) =>
        Array(access, record + AllocationBeginOffset, record + AllocationEndOffset);

    private static int[] Array(LsmfAccess access, int beginOffset, int endOffset)
    {
        var bytes = access.Model.Bytes;
        var range = access.Array(BinaryData.U64(bytes.Span, beginOffset), BinaryData.U64(bytes.Span, endOffset), sizeof(int));
        return Enumerable.Range(0, range.Count).Select(index => BinaryData.I32(bytes.Span, range.Offset + index * sizeof(int))).ToArray();
    }
}
