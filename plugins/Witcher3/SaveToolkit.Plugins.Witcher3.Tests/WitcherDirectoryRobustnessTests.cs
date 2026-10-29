using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherDirectoryRobustnessTests
{
    [Theory]
    [InlineData("RB")]
    [InlineData("NM")]
    [InlineData("MANU interior")]
    [InlineData("cross RB")]
    public void VariableIndexCannotInterpretMetadataAsGameVariables(string region)
    {
        var payload = RobustnessSave.Payload([0, 0, 0, 0], new Sav3Entry(16, 4));
        var rb = RobustnessSave.Roots(payload);
        var nm = RobustnessSave.Names(payload);
        var table = RobustnessSave.Table(payload);
        var offset = region switch { "RB" => rb, "NM" => nm, "MANU interior" => nm + 4, _ => 16 };
        var size = region == "cross RB" ? rb - offset + 1 : 1;
        RobustnessSave.WriteInt(payload, table + 4, offset + SyntheticSave.HeaderSize);
        RobustnessSave.WriteInt(payload, table + 8, size);
        Assert.Throws<SaveFormatException>(() => RobustnessSave.Open(payload));
    }

    [Fact]
    public void VariableAndCompleteManuBoundaryRemainSupportedWithOpaqueData()
    {
        var payload = RobustnessSave.Payload([1, 2, 3, 4], new Sav3Entry(16, 4));
        var bytes = SyntheticSave.Wrap(payload);
        using var document = WitcherTestFiles.Open(bytes);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        var container = WitcherContainer.Parse(bytes);
        var directory = Sav3Directory.Parse(container);
        Assert.Equal(3, directory.Names.Count);
        Assert.Equal(["Opaque", "MANU"], Sav3Tree.Parse(payload, directory).Nodes.Select(n => n.Tag));
    }

    [Theory]
    [InlineData("names count", -1)]
    [InlineData("names count", 0)]
    [InlineData("names count", 65536)]
    [InlineData("names count", int.MaxValue)]
    [InlineData("roots count", -1)]
    [InlineData("roots count", int.MaxValue)]
    [InlineData("index count", -1)]
    [InlineData("index count", 0)]
    [InlineData("index count", 200001)]
    [InlineData("index count", int.MaxValue)]
    [InlineData("root pointer", -1)]
    [InlineData("root pointer", int.MaxValue)]
    [InlineData("names pointer", -1)]
    [InlineData("names pointer", int.MaxValue)]
    [InlineData("index pointer", -1)]
    [InlineData("index pointer", int.MaxValue)]
    public void DirectoryCountsAndVirtualPointersRejectBeforeTraversal(string field, int value)
    {
        var payload = RobustnessSave.Payload([1, 2, 3, 4], new Sav3Entry(16, 4));
        var table = RobustnessSave.Table(payload);
        var offset = field switch
        {
            "names count" => RobustnessSave.Names(payload) + 6,
            "roots count" => RobustnessSave.Roots(payload) + 2,
            "index count" => table,
            "root pointer" => table - 6,
            "names pointer" => table - 10,
            _ => payload.Length - 6,
        };
        RobustnessSave.WriteInt(payload, offset, value);
        Assert.Throws<SaveFormatException>(() => RobustnessSave.Open(payload));
    }

    [Theory]
    [InlineData("length")]
    [InlineData("ASCII")]
    [InlineData("terminator")]
    [InlineData("SC")]
    [InlineData("MANU span")]
    public void NamesAndIndexTerminatorsAreBounded(string field)
    {
        var payload = RobustnessSave.Payload([1, 2, 3, 4], new Sav3Entry(16, 4));
        var nm = RobustnessSave.Names(payload);
        var table = RobustnessSave.Table(payload);
        switch (field)
        {
            case "length": payload[nm + 14] = byte.MaxValue; break;
            case "ASCII": payload[nm + 15] = 0xFF; break;
            case "terminator": payload[table - 11] = 0; break;
            case "SC": payload[table - 2] = 0; break;
            default: RobustnessSave.WriteInt(payload, table + 16, table - 10 - nm - 3); break;
        }
        Assert.Throws<SaveFormatException>(() => RobustnessSave.Open(payload));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("crossing")]
    [InlineData("negative size")]
    [InlineData("empty size")]
    [InlineData("header offset")]
    public void InvalidIndexedSpansHaveFormatErrors(string corruption)
    {
        var payload = WitcherContainer.Parse(SyntheticSave.Create()).Payload;
        var table = RobustnessSave.Table(payload);
        switch (corruption)
        {
            case "duplicate": RobustnessSave.WriteInt(payload, table + 12, SyntheticSave.HeaderSize + SyntheticSave.RootOffset); break;
            case "crossing": RobustnessSave.WriteInt(payload, table + 16, 20); break;
            case "negative size": RobustnessSave.WriteInt(payload, table + 16, -1); break;
            case "empty size": RobustnessSave.WriteInt(payload, table + 16, 0); break;
            default: RobustnessSave.WriteInt(payload, table + 12, SyntheticSave.HeaderSize + 4); break;
        }
        Assert.Throws<SaveFormatException>(() => RobustnessSave.Open(payload));
    }
}
