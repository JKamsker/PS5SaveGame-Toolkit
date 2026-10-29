using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class CurrencyCorpusTests
{
    // Derived scalar observations only; no save bytes, GUIDs or campaign IDs.
    [Theory]
    [InlineData("i29slot0-s-autosave", 0, 320640, 109, 725, 630, 263)]
    [InlineData("i29slot0-s-autosave", 1, 2417, 20, 23, 22, 9)]
    [InlineData("i29slot0-s-snapshot-0", 0, 320640, 109, 725, 630, 263)]
    [InlineData("i29slot0-s-snapshot-1000115474", 0, 2867, 5, 0, 0, 0)]
    [InlineData("i29slot0-s-snapshot-104506980", 0, 131417, 72, 398, 337, 152)]
    [InlineData("i29slot0-s-snapshot-108976056", 0, 37856, 72, 398, 337, 152)]
    [InlineData("i29slot0-s-snapshot-1099990005", 0, 998, 11, 23, 22, 9)]
    [InlineData("i29slot0-s-snapshot-1106495269", 0, 2789, 6, 23, 22, 9)]
    [InlineData("i29slot0-s-snapshot-1177913416", 0, 305709, 109, 725, 630, 263)]
    [InlineData("i29slot0-s-snapshot-1412875998", 0, 3198, 5, 0, 0, 0)]
    [InlineData("i29slot0-s-snapshot-1506250198", 0, 2725, 19, 23, 22, 9)]
    [InlineData("i29slot0-s-snapshot-1529440392", 0, 296386, 109, 725, 630, 263)]
    [InlineData("i29slot0-s-snapshot-1587540500", 0, 3238, 12, 13, 12, 6)]
    [InlineData("i29slot0-s-snapshot-1623926022", 0, 305709, 109, 725, 630, 263)]
    [InlineData("i29slot0-s-snapshot-1747363763", 0, 71004, 72, 398, 337, 152)]
    [InlineData("i29slot0-s-snapshot-1777266847", 0, 305709, 109, 725, 630, 263)]
    [InlineData("i29slot0-s-snapshot-1802099279", 0, 2213, null, 0, 0, 0)]
    [InlineData("i29slot0-s-snapshot-1813725300", 0, 4766, 7, 0, 0, 0)]
    [InlineData("i29slot0-s-snapshot-1867211690", 0, 151092, 94, 398, 337, 152)]
    [InlineData("i29slot0-s-snapshot-1984049680", 0, 125713, 72, 398, 337, 152)]
    [InlineData("i29slot0-s-snapshot-1992350878", 0, 2696, 10, 0, 0, 0)]
    [InlineData("i29slot0-s-snapshot-2005770781", 0, 54446, 72, 398, 337, 152)]
    [InlineData("i29slot0-s-snapshot-2127947730", 0, 2215, 5, 13, 12, 6)]
    [InlineData("i29slot0-s-snapshot-256473048", 0, 2157, 6, 23, 22, 9)]
    [InlineData("i29slot0-s-snapshot-339046575", 0, 2044, 14, 23, 22, 9)]
    [InlineData("i29slot0-s-snapshot-353949189", 0, 315, null, 13, 12, 6)]
    [InlineData("i29slot0-s-snapshot-379696474", 0, 2971, 10, 13, 12, 6)]
    [InlineData("i29slot0-s-snapshot-381373439", 0, 296386, 109, 725, 630, 263)]
    [InlineData("i29slot0-s-snapshot-505121150", 0, 54446, 72, 398, 337, 152)]
    [InlineData("i29slot0-s-snapshot-522295077", 0, 99293, 72, 398, 337, 152)]
    [InlineData("i29slot0-s-snapshot-536658292", 0, 1945, 6, 23, 22, 9)]
    [InlineData("i29slot0-s-snapshot-660216862", 0, 317641, 109, 725, 630, 263)]
    [InlineData("i29slot0-s-snapshot-723348676", 0, 4866, 7, 0, 0, 0)]
    [InlineData("i29slot0-s-snapshot-867870254", 0, 310505, 109, 725, 630, 263)]
    [InlineData("i29slot1-s-autosave", 0, 320640, 109, 725, 630, 263)]
    [InlineData("i29slot1-s-snapshot-412553368", 0, 320640, 109, 725, 630, 263)]
    public void EveryGameplayCopyPinsCurrencyAndUpgradeObservations(string directory, int generation,
        long bolts, int? raritanium, int gridBits, int modCounts, int modBits)
    {
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles.Where(file =>
            file.FileName == "i29-s.save" && file.Directory.Name == directory && file.Directory.Generation == generation).ToArray();
        Assert.Equal(2, files.Length);
        Assert.Single(files, file => file.Directory.IsBackup);
        foreach (var file in files)
        {
            var original = file.ReadAllBytes();
            var container = Dat1Container.Parse(original);
            var blocks = InventoryReader.Read(container);
            var currencyBlock = Assert.Single(blocks, block => block.Key == "78cb152e/0");
            using var document = new InsomniacPlugin().Open(file);
            var fields = CurrencyDiscoveryTests.CurrencyFields(document);
            var tree = new RiftEvidenceDecoder().Decode(file, new DecodeLimits());
            Assert.False(tree.Truncated);
            Assert.Equal(bolts, fields[RiftCurrencyView.BoltId].Value.AsInt64());
            CheckCount(RiftCurrencyView.BoltConfig, bolts);
            if (raritanium is { } balance)
            {
                Assert.Equal(balance, fields[RiftCurrencyView.RaritaniumId].Value.AsInt64());
                CheckCount(RiftCurrencyView.RaritaniumConfig, balance);
            }
            else
            {
                Assert.DoesNotContain(RiftCurrencyView.RaritaniumId, fields.Keys);
                Assert.DoesNotContain(currencyBlock.Records, record => record.ConfigBytes == RiftCurrencyView.RaritaniumConfig);
            }
            var upgrades = blocks.SelectMany(block => block.Records).Select(record => record.Upgrades)
                .OfType<WeaponUpgrades>().ToArray();
            Assert.Equal(gridBits, upgrades.Sum(upgrade => upgrade.Grid.Sum(row => BitOperations.PopCount((uint)row))));
            Assert.Equal(modCounts, upgrades.Sum(upgrade => upgrade.Counts.Sum(value => (int)value)));
            Assert.Equal(modBits, upgrades.Sum(upgrade => BitOperations.PopCount(upgrade.Mods)));
            Assert.All(upgrades, upgrade => Assert.False(upgrade.Omega));
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());

            void CheckCount(string config, long expected)
            {
                var record = Assert.Single(currencyBlock.Records, record => record.ConfigBytes == config);
                Assert.Equal(0x4E5CE3CBu, record.Type);
                Assert.Equal(23, record.Size);
                Assert.Equal(0x0F, original[record.Offset]);
                Assert.Equal((uint)expected, BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(record.Offset + 17)));
                Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), Assert.Single(tree.Values,
                    value => value.Path == $"inventory/{currencyBlock.Key}/{record.Key}/count").Value);
                Assert.True(fields.Values.All(field => field.Spec.ReadOnly));
            }
        }
    }

    [Fact]
    public void CounterSubtractionAndAggregateUpgradeCountsAreNotWalletOrCostMappings()
    {
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        var campaign = PlayerCorpusTests.Fields(file, RiftPlayerView.CampaignTag);
        using var document = new InsomniacPlugin().Open(file);
        var fields = CurrencyDiscoveryTests.CurrencyFields(document);
        Assert.Equal(716220, document.GetValue(RiftScalarWrites.BalanceId).AsInt64());
        Assert.NotEqual(document.GetValue(RiftScalarWrites.BalanceId), fields[RiftCurrencyView.BoltId].Value);
        Assert.Equal(109, fields[RiftCurrencyView.RaritaniumId].Value.AsInt64());
        Assert.True(campaign["CareerRaritanium"].Values[0].AsDouble() < campaign["CareerRaritaniumSpend"].Values[0].AsDouble());
        var upgrades = InventoryReader.Read(Dat1Container.Parse(file.ReadAllBytes())).SelectMany(block => block.Records)
            .Select(record => record.Upgrades).OfType<WeaponUpgrades>().ToArray();
        Assert.Equal(584, campaign["CareerRaritaniumSpend"].Values[0].AsInt64());
        Assert.Equal(630, upgrades.Sum(upgrade => upgrade.Counts.Sum(value => (int)value)));
        Assert.Equal(725, upgrades.Sum(upgrade => upgrade.Grid.Sum(row => BitOperations.PopCount((uint)row))));
    }
}
