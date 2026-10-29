using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class CurrencyDiscoveryTests
{
    [Theory]
    [InlineData("configs/pickup/pup_bolt_item.config", "947dc517d343ad64")]
    [InlineData("configs/pickup/pup_raritanium_item.config", "95ffa4c1254a6f29")]
    public void PublicCurrencyPathsMatchThePs5ConfigTokens(string path, string expected) =>
        Assert.Equal(expected, RiftAssetId.Hash(path));

    [Theory]
    [InlineData(123456u, 37u)]
    [InlineData(0u, uint.MaxValue)]
    public void SyntheticCurrencyRecordsExposeExactReadOnlyCountsAndPreserveEveryByte(uint bolts, uint raritanium)
    {
        var bytes = Fixture((RiftCurrencyView.BoltConfig, bolts), (RiftCurrencyView.RaritaniumConfig, raritanium));
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", bytes, "PPSA01474"));
        var fields = CurrencyFields(document);
        Assert.Equal(bolts, fields[RiftCurrencyView.BoltId].Value.AsUInt32());
        Assert.Equal(raritanium, fields[RiftCurrencyView.RaritaniumId].Value.AsUInt32());
        Assert.All(fields.Values, field =>
        {
            Assert.True(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Contains("Tkachov", field.Description);
            Assert.Contains("HUD", field.Warning);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
            Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(1L))]).Succeeded);
        });
        Assert.False(document.CanEdit);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrAmbiguousRaritaniumIsUnresolvedRatherThanZero(bool duplicate)
    {
        var bytes = duplicate
            ? Fixture((RiftCurrencyView.RaritaniumConfig, 7u), (RiftCurrencyView.RaritaniumConfig, 8u))
            : Fixture((RiftCurrencyView.BoltConfig, 7u));
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", bytes, "PPSA01474"));
        var fields = CurrencyFields(document);
        Assert.DoesNotContain(RiftCurrencyView.RaritaniumId, fields.Keys);
        Assert.Throws<KeyNotFoundException>(() => document.GetValue(RiftCurrencyView.RaritaniumId));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ForeignTitleAndUnsupportedPresenceMaskDoNotGainCurrencySemantics()
    {
        var bytes = Fixture((RiftCurrencyView.BoltConfig, uint.MaxValue));
        using var foreign = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", bytes));
        Assert.DoesNotContain(Assert.IsType<TabsNode>(foreign.BuildView()).Tabs, tab => tab.Title == "Currencies");
        bytes[56] = 0x07;
        using var unsupported = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", bytes, "PPSA01474"));
        Assert.Empty(CurrencyFields(unsupported));
        Assert.Equal(bytes, Assert.Single(unsupported.Serialize()).Content.ToArray());
    }

    [Fact]
    public void CareerCounterEditsAndTargetPlansLeaveTheInventoryWalletUnchanged()
    {
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        using var document = new InsomniacPlugin().Open(file);
        var fields = CurrencyFields(document);
        Assert.Equal(320640, fields[RiftCurrencyView.BoltId].Value.AsInt64());
        var player = Assert.IsType<TabsNode>(document.BuildView()).Tabs[0];
        var diagnostic = Assert.Single(player.Children.OfType<FieldNode>(), f => f.ValueId == RiftScalarWrites.BalanceId);
        Assert.Contains("diagnostic", diagnostic.Label, StringComparison.OrdinalIgnoreCase);
        var planner = (IActionPlanningDocument)document;
        var parameters = new Dictionary<string, FieldValue> { [RiftBoltPlanning.TargetParameter] = FieldValue.Of(1L) };
        var plan = planner.PlanAction(RiftBoltPlanning.ActionId, parameters, new());
        Assert.Contains(plan.Warnings, warning => warning.Contains("inventory", StringComparison.Ordinal));
        Assert.True(planner.ApplyActionPlan(RiftBoltPlanning.ActionId, parameters, new(), plan).Succeeded);
        Assert.Equal(320640, document.GetValue(RiftCurrencyView.BoltId).AsInt64());
        using var reopened = new InsomniacPlugin().Open(MemorySaveSet.SingleFile(file.FileName,
            Assert.Single(document.Serialize()).Content.ToArray(), "PPSA01474"));
        Assert.Equal(fields[RiftCurrencyView.BoltId].Value, CurrencyFields(reopened)[RiftCurrencyView.BoltId].Value);
    }

    internal static Dictionary<string, FieldNode> CurrencyFields(ISaveDocument document) =>
        Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "Currencies")
            .Children.OfType<FieldNode>().ToDictionary(field => field.ValueId);

    internal static byte[] Fixture(params (string Config, uint Count)[] items)
    {
        const int sectionStart = 32;
        const int recordSize = 31;
        const int blockHeaderSize = 16;
        var bytes = new byte[sectionStart + blockHeaderSize + items.Length * recordSize];
        Word(0, 0x44415431); Word(4, 8); Word(8, (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 1);
        Word(16, InventoryReader.SectionTag); Word(20, sectionStart); Word(24, (uint)(bytes.Length - sectionStart));
        Word(sectionStart, 0x78CB152E); Word(sectionStart + 4, (uint)(bytes.Length - sectionStart - 8));
        Word(sectionStart + 8, InventoryReader.Magic); Word(sectionStart + 12, (uint)items.Length);
        for (var index = 0; index < items.Length; index++)
        {
            var start = sectionStart + blockHeaderSize + index * recordSize;
            Word(start, 0x4E5CE3CB); Word(start + 4, 23);
            bytes[start + 8] = 0x0F;
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(start + 9), (ulong)index + 1);
            Convert.FromHexString(items[index].Config).CopyTo(bytes, start + 17);
            Word(start + 25, items[index].Count);
        }
        return bytes;
        void Word(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }
}
