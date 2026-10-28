using SaveToolkit.Sdk;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class SuckerPunchFormatTests
{
    [Fact]
    public void EmbeddedCatalogsAreAttributedAndUseStableRawIds()
    {
        var plugin = new SuckerPunchPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02225");
        foreach (var id in new[] { "suckerpunch_items", "suckerpunch_equipment", "suckerpunch_techniques" })
        {
            var catalog = context.Catalog(id);
            Assert.NotNull(catalog);
            Assert.NotEmpty(catalog.Dataset.Attribution.SourceUrl);
            Assert.NotEmpty(catalog.Dataset.Attribution.License);
            Assert.All(catalog.Search(new ReferenceQuery { Limit = 200 }).Items,
                record => Assert.True(record.Attributes.ContainsKey("evidence")));
        }
    }

    [Fact]
    public void TsushimaSamplesOpenAndExposeVerifiedHeader()
    {
        var plugin = new SuckerPunchPlugin();
        var set = SampleSaves.Load("PPSA02225");
        var files = set.AllDiskFiles.Where(f => plugin.Probe(f).Score == ProbeScore.Title).ToList();

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var document = plugin.Open(file);
            var tabs = (TabsNode)document.BuildView();
            Assert.Equal(FieldValue.Of((ulong)0x14E), document.GetValue("container/word0"));
            var header = (SectionNode)tabs.Tabs[0].Children[1];
            Assert.Equal(Confidence.Verified, ((FieldNode)header.Children[0]).Confidence);
        }
    }

    [Fact]
    public void TsushimaChecksumCorruptionIsRejected()
    {
        var plugin = new SuckerPunchPlugin();
        var source = SampleSaves.Load("PPSA02225").AllDiskFiles.First(f => f.FileName.Equals("dis.dat", StringComparison.OrdinalIgnoreCase));
        var bytes = source.ReadAllBytes();
        bytes[0x200] ^= 0x01;
        var file = new MemorySaveSet()
            .With("PPSA02225", "corrupt", new Dictionary<string, byte[]> { ["dis.dat"] = bytes })
            .Directories[0].Files[0];

        Assert.Throws<SaveFormatException>(() => plugin.Open(file));
    }

    [Fact]
    public void YoteiEmptyPlaceholderIsAcceptedAsOpaque()
    {
        var plugin = new SuckerPunchPlugin();
        var file = new MemorySaveSet()
            .With("PPSA26344", "empty", new Dictionary<string, byte[]> { ["dis.dat"] = new byte[616_448] })
            .Directories[0].Files[0];

        using var document = plugin.Open(file);
        _ = document.BuildView();
        Assert.Equal(FieldValue.Of(0UL), document.GetValue("container/word0"));
    }

    [Fact]
    public void SerializePreservesEveryClaimedSampleByte()
    {
        var plugin = new SuckerPunchPlugin();
        foreach (var title in new[] { "PPSA02225", "PPSA26344" })
        {
            foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(f => plugin.Probe(f).Score == ProbeScore.Title))
            {
                using var document = plugin.Open(file);
                var output = Assert.Single(document.Serialize());
                Assert.Equal(file.RelativePath, output.RelativePath);
                Assert.True(output.Content.Span.SequenceEqual(file.ReadAllBytes()), file.RelativePath);
            }
        }
    }
}
