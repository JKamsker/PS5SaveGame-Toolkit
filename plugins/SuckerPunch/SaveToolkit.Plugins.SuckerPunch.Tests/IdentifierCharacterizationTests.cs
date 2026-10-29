using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class IdentifierCharacterizationTests
{
    private const string AllowedCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedTablesAcceptExactlyAsciiIdentifierBytes(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        var nameOffset = save.Inventory[SyntheticSave.FirstUnknown] - SyntheticSave.FirstUnknown.Length;
        var secondaryOffset = yotei ? save.Secondary[SyntheticSave.FirstUnknown] - SyntheticSave.FirstUnknown.Length : 0;
        for (var value = 0; value <= byte.MaxValue; value++)
        {
            save.Bytes[nameOffset] = (byte)value;
            if (yotei) save.Bytes[secondaryOffset] = (byte)value;
            save.Repair();
            if (!AllowedCharacters.Contains((char)value, StringComparison.Ordinal))
            {
                Assert.Equal(yotei ? "Invalid Yōtei record identifier." : "Invalid Tsushima inventory identifier.",
                    Assert.Throws<SaveFormatException>(() => save.Open()).Message);
                continue;
            }
            using var document = save.Open();
            Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
            var expectedName = (char)value + SyntheticSave.FirstUnknown[1..];
            var table = yotei ? YoteiTestData.Inventory(document) : SuckerPunchTestData.Inventory(document);
            Assert.Contains(table.Rows, row => row.Key == expectedName);
        }
    }

    [Fact]
    public void IdentifierScannerUsesTheSameAsciiAlphabetWithoutChangingClassification()
    {
        var save = new SyntheticSave(false);
        const int offset = SyntheticSave.HeaderSize;
        "\x07\0\0\0Armor_A"u8.CopyTo(save.Bytes.AsSpan(offset));
        for (var value = 0; value <= byte.MaxValue; value++)
        {
            save.Bytes[offset + sizeof(uint) + "Armor_".Length] = (byte)value;
            save.Repair();
            using var document = save.Open();
            var queryable = (IQueryableDocument)document;
            var page = queryable.Query("equipment/identifiers", new() { Limit = 10 });
            Assert.Equal(AllowedCharacters.Contains((char)value, StringComparison.Ordinal) ? 1 : 0, page.TotalCount);
            if (page.TotalCount == 1) Assert.Equal("Armor_" + (char)value, Assert.Single(page.Rows).Key);
            Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }
}
