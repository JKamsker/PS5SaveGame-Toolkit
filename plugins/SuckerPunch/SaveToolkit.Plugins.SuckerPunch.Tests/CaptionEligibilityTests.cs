using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class CaptionEligibilityTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("duplicate")]
    [InlineData("absent")]
    [InlineData("format")]
    [InlineData("encoding")]
    [InlineData("terminator")]
    [InlineData("global")]
    [InlineData("oversize")]
    public void InvalidMetadataIsPreservedWithoutOwnershipOrAction(string kind)
    {
        byte[]? sfo = CaptionTestData.Sfo();
        switch (kind)
        {
            case "missing": sfo = null; break;
            case "malformed": sfo[0] ^= 0xFF; break;
            case "duplicate": "SUBTITLE\0"u8.CopyTo(sfo.AsSpan(61)); break;
            case "absent": sfo = CaptionTestData.Sfo(key: "NOTTITLE"); break;
            case "format": sfo = CaptionTestData.Sfo(format: 0x0404); break;
            case "encoding": sfo[CaptionTestData.DataStart] = 0xFF; break;
            case "terminator": sfo[CaptionTestData.DataStart + CaptionTestData.Original.Length] = 0xFF; break;
            case "global": sfo = CaptionTestData.Sfo(new string('a', 4096), 8192); break;
            case "oversize": sfo = new byte[1024 * 1024 + 1]; break;
        }
        using var document = SuckerPunchTestData.Open(CaptionTestData.File(sfo));
        var view = (TabsNode)document.BuildView();
        Assert.Empty(view.Tabs[0].Children.OfType<ActionNode>());
        Assert.Contains(view.Tabs[0].Children.OfType<TextNode>(), node => node.Text.StartsWith("Save caption unavailable:", StringComparison.Ordinal));
        Assert.Empty(((IAdditionalOutputOwner)document).AdditionalOutputPaths);
        Assert.Single(document.Serialize());
        Assert.NotEmpty(CaptionTestData.Plan(document, "new").Errors);
        Assert.Empty(((IFieldMetadataDocument)document).ResolveFieldMetadata([CaptionTestData.ValueId]));
    }

    [Theory]
    [InlineData(46, "dis.dat", false)]
    [InlineData(50, "dis.dat", false)]
    [InlineData(49, "gamestate/dis.dat", false)]
    [InlineData(49, "dis.dat", true)]
    public void UnsupportedVersionRoleAndInspectionOnlyInventoryCannotClaimSfo(int version, string role, bool removeInventory)
    {
        var save = new SyntheticSave(false);
        save.Write(12, (uint)version);
        if (removeInventory) save.Bytes[save.AnchorStart + sizeof(uint)] ^= 1;
        save.Repair();
        using var document = SuckerPunchTestData.Open(CaptionTestData.File(CaptionTestData.Sfo(), save, role));
        Assert.Empty(((IAdditionalOutputOwner)document).AdditionalOutputPaths);
        Assert.NotEmpty(CaptionTestData.Plan(document, "new").Errors);
    }

    [Fact]
    public void DuplicateCaseInsensitiveSystemPathsNeverSilentlySelectFirst()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["dis.dat"] = new SyntheticSave(false).Bytes,
            [CaptionTestData.SfoPath] = CaptionTestData.Sfo(),
            ["SCE_SYS/PARAM.SFO"] = CaptionTestData.Sfo("Other"),
        };
        var file = new SaveToolkit.Testing.MemorySaveSet().With("PPSA02225", "CAPTION", files).Directories[0].Files[0];
        using var document = SuckerPunchTestData.Open(file);
        Assert.Empty(((IAdditionalOutputOwner)document).AdditionalOutputPaths);
        Assert.Contains(CaptionTestData.Plan(document, "new").Errors,
            error => error.Message.Contains("duplicate", StringComparison.Ordinal));
        Assert.Single(document.Serialize());
    }

    [Fact]
    public void YoteiRemainsOutsideCaptionScope()
    {
        using var document = SuckerPunchTestData.Open(CaptionTestData.File(CaptionTestData.Sfo(), new SyntheticSave(true), title: "PPSA26344"));
        Assert.IsNotType<IAdditionalOutputOwner>(document, exactMatch: false);
        Assert.NotEmpty(CaptionTestData.Plan(document, "new").Errors);
    }
}
