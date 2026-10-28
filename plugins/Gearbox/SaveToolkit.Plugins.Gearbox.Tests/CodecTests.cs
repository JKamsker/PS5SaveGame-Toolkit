using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class CodecTests
{
    [Theory]
    [InlineData(false, "2A87402772985D1784C57074725CF2CAC9E133BCE093E80C5B5F586C0B0922CAF15B9F556CE1A01017B29DF0381E6A7F63826680CDFB0DBF767FAB1D4E53C698")]
    [InlineData(true, "1711E4501E7A3D158A2AC01ADEC2D32297AEE9D88C52F5E74104F2607B0590448D3E406E62BE891392D521C1226D2855918D3A1D81234B3A37C6CC2F40AD94D9")]
    public void Ps4Masks_MatchIndependentFeedbackVector(bool profile, string expected)
    {
        var plain = Enumerable.Range(0, 64).Select(v => (byte)v).ToArray();
        Assert.Equal(Convert.FromHexString(expected), BogoCrypt.Encode(plain, profile));
        Assert.Equal(plain, BogoCrypt.Decode(Convert.FromHexString(expected), profile));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Codec_CoversEmptyPartialAndMultipleWindows(bool profile)
    {
        foreach (var length in new[] { 0, 1, 31, 32, 33, 1000 })
        {
            var plain = Enumerable.Range(0, length).Select(v => (byte)(v * 71)).ToArray();
            Assert.Equal(plain, BogoCrypt.Decode(BogoCrypt.Encode(plain, profile), profile));
        }
    }

    [Fact]
    public void WireEdits_PreserveUnknownRecordsAndNoncanonicalVarints()
    {
        // Unknown repeated #99 values, noncanonical known hash #1, known quantity #2.
        byte[] original = [0x98, 0x06, 0x81, 0x00, 0x08, 0x81, 0x00, 0x10, 0x07, 0x98, 0x06, 0x02];
        var parsed = ProtoMessage.Parse(original, GearboxSchema.Bl3, "InventoryCategorySaveData");
        Assert.Equal(original, parsed.Encode());
        parsed.SetInteger("quantity", 999);
        var encoded = parsed.Encode();
        var reopened = ProtoMessage.Parse(encoded, GearboxSchema.Bl3, "InventoryCategorySaveData");
        Assert.Equal(999, reopened.Integer("quantity"));
        Assert.Equal(original[..7], encoded[..7]);
        Assert.Equal(original[^3..], encoded[^3..]);
        parsed.SetInteger("quantity", 7);
        Assert.Equal(original, parsed.Encode());
    }

    [Fact]
    public void NestedEdit_PreservesUnknownRecordsAndChangesOnlyEnclosingLengths()
    {
        // Character #9 currency message: hash 1, quantity 7, unknown #99=42; unknown root #99=5.
        byte[] original = [0x4A, 0x07, 0x08, 0x01, 0x10, 0x07, 0x98, 0x06, 0x2A, 0x98, 0x06, 0x05];
        var parsed = ProtoMessage.Parse(original, GearboxSchema.Bl3, "Character");
        parsed.Child("inventory_category_list")!.SetInteger("quantity", 1_000_000);
        var encoded = parsed.Encode();
        var reopened = ProtoMessage.Parse(encoded, GearboxSchema.Bl3, "Character");
        Assert.Equal(1_000_000, reopened.Child("inventory_category_list")!.Integer("quantity"));
        Assert.Equal(original[^3..], encoded[^3..]);
        Assert.Equal(original[6..9], reopened.Child("inventory_category_list")!.Fields[^1].Original);
        parsed.Child("inventory_category_list")!.SetInteger("quantity", 7);
        Assert.Equal(original, parsed.Encode());
    }

    [Theory]
    [InlineData("00")]
    [InlineData("0F")]
    [InlineData("0980")]
    [InlineData("4A050801")]
    [InlineData("BA0201FF")]
    [InlineData("620180")]
    public void MalformedProtobuf_ThrowsSaveFormatException(string hex) =>
        Assert.Throws<SaveFormatException>(() => ProtoMessage.Parse(Convert.FromHexString(hex), GearboxSchema.Bl3, "Character"));

    [Fact]
    public void References_UseSeparateSchemasAndKnownXpThresholds()
    {
        Assert.Equal(53, GearboxSchema.Bl3.Tag("Character", "game_state_save_data_for_playthrough"));
        Assert.Equal(19, GearboxSchema.Tina.Tag("Character", "game_state_save_data_for_playthrough"));
        Assert.Equal(137, GearboxSchema.Bl3.Tag("Profile", "bank_inventory_list"));
        Assert.Equal(212, GearboxSchema.Tina.Tag("Profile", "bank_inventory_list"));
        Assert.Equal(72, GearboxSchema.Bl3.Level(9_520_932));
        Assert.Equal(5, GearboxSchema.Bl3.Level(6494));
        Assert.Equal(12, GearboxSchema.Tina.Level(70754));
        Assert.Equal(13, GearboxSchema.Tina.Level(85433));
    }

    [Fact]
    public void GoldenKeyHash_MatchesPublishedInventoryPathHashAlgorithm()
    {
        const string path = "/Game/Gear/_Shared/_Design/InventoryCategories/InventoryCategory_GoldenKey.InventoryCategory_GoldenKey";
        uint hash = 0;
        foreach (var value in Encoding.Unicode.GetBytes(path.ToUpperInvariant()))
        {
            hash ^= value;
            for (var bit = 0; bit < 8; bit++) hash = (hash >> 1) ^ ((hash & 1) != 0 ? 0xEDB88320U : 0);
        }
        Assert.Equal(GearboxOverview.GoldenKeyHash, hash);
    }
}
