using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class SyntheticContainerCorruptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PhysicalPrefixesRejectAtHeaderAndNamedRecordBoundaries(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        var boundaries = Enumerable.Range(0, SyntheticSave.HeaderSize + 1).ToHashSet();
        foreach (var rows in new[] { save.Inventory, save.Secondary, save.Statistics, save.Gear })
            foreach (var (name, data) in rows)
            {
                boundaries.Add(data - name.Length - sizeof(uint));
                boundaries.Add(data - name.Length - 1);
                boundaries.Add(data - 1);
                boundaries.Add(data);
            }
        boundaries.UnionWith([save.ContentEnd - 1, save.ContentEnd, save.Bytes.Length - 1]);
        var plugin = new SuckerPunchPlugin();
        var decoder = new HookSaveDecoder(Assert.Single(((ISaveDecoderProvider)plugin).Decoders));
        foreach (var length in boundaries.Order())
        {
            var file = SuckerPunchTestData.WithBytes(save.Bytes[..length],
                yotei ? YoteiTestData.Title : SuckerPunchTestData.TsushimaTitle);
            Assert.False(plugin.Probe(file).IsMatch);
            Assert.False(decoder.CanDecode(file));
            Assert.Equal("This is not a recognised Sucker Punch save file.",
                Assert.Throws<SaveFormatException>(() => plugin.Open(file)).Message);
            Assert.Equal("This is not a recognised Sucker Punch save file.",
                Assert.Throws<SaveFormatException>(() => decoder.Decode(file, new DecodeLimits())).Message);
        }
    }

    [Theory]
    [InlineData(false, "first-body")]
    [InlineData(false, "last-body")]
    [InlineData(false, "checksum")]
    [InlineData(false, "tail")]
    [InlineData(false, "header-word")]
    [InlineData(true, "first-body")]
    [InlineData(true, "last-body")]
    [InlineData(true, "checksum")]
    [InlineData(true, "tail")]
    [InlineData(true, "header-word")]
    public void ChecksumRegionsUseDocumentedRejectionOrInspectionPolicy(bool yotei, string region)
    {
        var save = new SyntheticSave(yotei);
        var offset = region switch
        {
            "first-body" => SyntheticSave.HeaderSize,
            "last-body" => save.ContentEnd - 1,
            "checksum" => save.ChecksumOffset,
            "tail" => save.ContentEnd,
            "header-word" => yotei ? 4 : 12,
            _ => throw new ArgumentOutOfRangeException(nameof(region)),
        };
        save.Bytes[offset] ^= 1;
        var included = region != "tail" && !(yotei && region == "header-word");
        if (included && !yotei)
        {
            Assert.Equal("Ghost of Tsushima save checksum is invalid.",
                Assert.Throws<SaveFormatException>(() => save.Open()).Message);
            return;
        }

        using var document = save.Open();
        Assert.Equal(!included, document.CanEdit);
        if (included)
        {
            var table = YoteiTestData.Inventory(document);
            Assert.All(table.Rows, row => Assert.True(Assert.IsType<FieldNode>(row.Cells["quantity"]).Spec.ReadOnly));
            Assert.False(document.ApplyEdits([new(save.ResourceId, FieldValue.Of(1L))]).Succeeded);
            Assert.Empty(((IActionPlanningDocument)document).PlanAction(save.ActionId,
                new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(1L) }, new ActionScope()).Changes);
        }
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclaredEndMayIncludeTheLastPhysicalByte(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        save.SetEnd(save.Bytes.Length);
        save.Repair();
        using var document = save.Open();
        Assert.True(document.CanEdit);
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        save.Bytes[^1] ^= 1;
        if (!yotei)
            Assert.Throws<SaveFormatException>(() => save.Open());
        else
        {
            using var mismatched = save.Open();
            Assert.False(mismatched.CanEdit);
            Assert.Equal(save.Bytes, Assert.Single(mismatched.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingLayoutUsesOnlyTheDocumentedFallback(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        save.Bytes[yotei ? save.InventoryStart : save.AnchorStart] ^= 1;
        save.Repair();
        if (yotei)
        {
            Assert.Equal("Yōtei secondary inventory table is missing.",
                Assert.Throws<SaveFormatException>(() => save.Open()).Message);
            return;
        }
        using var document = save.Open();
        Assert.False(document.CanEdit);
        Assert.DoesNotContain(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "Inventory");
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
