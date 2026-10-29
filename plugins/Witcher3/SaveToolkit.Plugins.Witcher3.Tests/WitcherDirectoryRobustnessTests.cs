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
        using var document = RobustnessSave.OpenContainer(bytes);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        var container = WitcherContainer.Parse(bytes);
        var directory = Sav3Directory.Parse(container);
        Assert.Equal(3, directory.Names.Count);
        Assert.Equal(["Opaque", "MANU"], Sav3Tree.Parse(payload, directory).Nodes.Select(n => n.Tag));
    }
}
