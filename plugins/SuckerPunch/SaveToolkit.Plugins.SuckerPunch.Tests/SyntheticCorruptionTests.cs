using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class SyntheticCorruptionTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 96)]
    [InlineData(true, 0)]
    [InlineData(true, 96)]
    public void IndependentNamedContainersOpenAtDifferentOffsetsAndRoundTripExactly(bool yotei, int prefixLength)
    {
        var save = new SyntheticSave(yotei, prefixLength);
        var file = save.File();
        Assert.True(new SuckerPunchPlugin().Probe(file).IsMatch);
        using var document = save.Open();
        Assert.True(document.CanEdit);
        var table = yotei ? YoteiTestData.Inventory(document) : SuckerPunchTestData.Inventory(document);
        Assert.Equal(save.Inventory.Keys, table.Rows.Select(row => row.Key));
        Assert.Equal(48, table.Rows.Count);
        Assert.Equal(SyntheticSave.InitialQuantity, document.GetValue(save.ResourceId).AsUInt32());
        Assert.Equal(save.Checksum(), SuckerPunchTestData.Word(save.Bytes, save.ChecksumOffset));
        if (yotei)
        {
            var tabs = Assert.IsType<TabsNode>(document.BuildView());
            var stats = Assert.IsType<TableNode>(Assert.Single(tabs.Tabs.Single(tab => tab.Title == "Statistics").Children));
            Assert.Equal(save.Statistics.Keys, stats.Rows.Select(row => row.Key));
            Assert.Equal(33, stats.Rows.Count);
            Assert.Equal(1037, Assert.Single(((IQueryableDocument)document).Collections).TotalCount);
        }
        var output = Assert.Single(document.Serialize());
        Assert.Equal(file.RelativePath, output.RelativePath);
        Assert.Equal(save.Bytes, output.Content.ToArray());
    }

    [Theory]
    [InlineData(false, "zero-length")]
    [InlineData(false, "large-length")]
    [InlineData(false, "identifier")]
    [InlineData(false, "duplicate")]
    [InlineData(false, "terminal-name")]
    [InlineData(false, "required-name")]
    [InlineData(true, "zero-length")]
    [InlineData(true, "large-length")]
    [InlineData(true, "identifier")]
    [InlineData(true, "duplicate")]
    [InlineData(true, "terminal-name")]
    [InlineData(true, "required-name")]
    public void NonAnchorInventoryCorruptionsReachTheirSpecificValidation(bool yotei, string mutation)
    {
        var save = new SyntheticSave(yotei);
        var name = SyntheticSave.SecondUnknown;
        var nameOffset = save.Inventory[name] - name.Length;
        string expected;
        switch (mutation)
        {
            case "zero-length":
            case "large-length":
                save.Write(nameOffset - sizeof(uint), mutation == "zero-length" ? 0 : uint.MaxValue);
                expected = yotei ? "Invalid Yōtei record name length." : "Invalid Tsushima inventory name length.";
                break;
            case "identifier":
                save.Bytes[nameOffset] = 0xFF;
                expected = yotei ? "Invalid Yōtei record identifier." : "Invalid Tsushima inventory identifier.";
                break;
            case "duplicate":
                Encoding.ASCII.GetBytes(SyntheticSave.FirstUnknown, save.Bytes.AsSpan(nameOffset));
                expected = yotei ? "Duplicate Yōtei record identifier." : "Duplicate Tsushima inventory identifier.";
                break;
            case "terminal-name":
                save.Bytes[save.Inventory[save.LastName] - save.LastName.Length] = (byte)'X';
                expected = yotei ? "Unrecognised Yōtei inventory layout." : "Unrecognised Tsushima inventory table layout.";
                break;
            case "required-name":
                save.Bytes[save.Inventory[save.ResourceName] - save.ResourceName.Length] = (byte)'X';
                expected = yotei ? "Unrecognised Yōtei inventory layout." : "Unrecognised Tsushima inventory table layout.";
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        save.Repair();
        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => save.Open()).Message);
    }

    [Theory]
    [InlineData(false, "length-word")]
    [InlineData(false, "name")]
    [InlineData(false, "trailer")]
    [InlineData(true, "length-word")]
    [InlineData(true, "name")]
    [InlineData(true, "trailer")]
    public void DeclaredEndBoundsEveryPartOfTheLastRecord(bool yotei, string part)
    {
        var save = new SyntheticSave(yotei);
        var data = (yotei ? save.Secondary : save.Inventory)[save.LastName];
        var end = part switch
        {
            "length-word" => data - save.LastName.Length - 1,
            "name" => data - 1,
            "trailer" => data + (yotei ? 16 : 20) - 1,
            _ => throw new ArgumentOutOfRangeException(nameof(part)),
        };
        save.SetEnd(end);
        save.Repair();
        // Physical bytes still exist: the parser must respect the declared boundary.
        var expected = yotei ? "Yōtei named table is truncated." : "Tsushima inventory table is truncated.";
        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => save.Open()).Message);
    }

    [Theory]
    [InlineData("statistics-ambiguous", "Yōtei statistics table is missing or ambiguous.")]
    [InlineData("gear-ambiguous", "Yōtei gear registry is missing or ambiguous.")]
    [InlineData("statistics-terminal", "Unrecognised Yōtei section boundaries.")]
    [InlineData("gear-terminal", "Unrecognised Yōtei section boundaries.")]
    [InlineData("registry-sequence", "Unrecognised Yōtei inventory layout.")]
    public void YoteiValidChecksumDoesNotHideMalformedRegistries(string mutation, string expected)
    {
        var save = new SyntheticSave(yotei: true);
        switch (mutation)
        {
            case "statistics-ambiguous":
                save.Bytes.AsSpan(save.StatisticsStart, 8 + "FishSpeared".Length).CopyTo(save.Bytes.AsSpan(SyntheticSave.HeaderSize));
                break;
            case "gear-ambiguous":
                save.Bytes.AsSpan(save.GearStart, 8 + "HalfShog101".Length).CopyTo(save.Bytes.AsSpan(SyntheticSave.HeaderSize));
                break;
            case "statistics-terminal":
                save.Bytes[save.Statistics[SyntheticSave.YoteiStatisticsLast] - SyntheticSave.YoteiStatisticsLast.Length] = (byte)'X';
                break;
            case "gear-terminal":
                save.Bytes[save.Gear[SyntheticSave.YoteiGearLast] - SyntheticSave.YoteiGearLast.Length] = (byte)'X';
                break;
            case "registry-sequence":
                save.Bytes[save.Secondary[SyntheticSave.SecondUnknown] - SyntheticSave.SecondUnknown.Length] = (byte)'X';
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        save.Repair();
        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => save.Open()).Message);
    }

    [Theory]
    [InlineData(false, 0U)]
    [InlineData(false, 15U)]
    [InlineData(false, 555_009U)]
    [InlineData(false, uint.MaxValue)]
    [InlineData(true, 0U)]
    [InlineData(true, 15U)]
    [InlineData(true, 616_449U)]
    [InlineData(true, uint.MaxValue)]
    public void InvalidDeclaredEndIsRejectedBeforeChecksumAndTables(bool yotei, uint end)
    {
        var save = new SyntheticSave(yotei);
        save.Write(yotei ? 8 : 4, end);
        var expected = yotei ? "Invalid Yōtei declared content end." : "Invalid Ghost of Tsushima container header.";
        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => save.Open()).Message);
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    [InlineData(true, 1)]
    public void ExactContainerSizeIsRequired(bool yotei, int delta)
    {
        var save = new SyntheticSave(yotei);
        var resized = new byte[save.Bytes.Length + delta];
        save.Bytes.AsSpan(0, Math.Min(resized.Length, save.Bytes.Length)).CopyTo(resized);
        var file = SuckerPunchTestData.WithBytes(resized, yotei ? YoteiTestData.Title : SuckerPunchTestData.TsushimaTitle);
        Assert.False(new SuckerPunchPlugin().Probe(file).IsMatch);
        var error = Assert.Throws<SaveFormatException>(() => new SuckerPunchPlugin().Open(file));
        Assert.Equal("This is not a recognised Sucker Punch save file.", error.Message);
    }
}
