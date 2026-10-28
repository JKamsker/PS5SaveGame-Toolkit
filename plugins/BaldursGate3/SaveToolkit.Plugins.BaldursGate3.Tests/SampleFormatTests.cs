using System.Security.Cryptography;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class SampleFormatTests
{
    private const string TitleId = "PPSA18463";

    [Fact]
    public void PackageHasKnownEntriesAndEveryResourceParses()
    {
        var package = LspkPackage.Read(Save().ReadAllBytes());
        Assert.Equal(18u, package.Version);
        string[] expectedEntries = ["meta.lsf", "StorySave.bin", "Globals.lsf", "SaveInfo.json", "AutoSave_0.WebP",
            "LevelCache/TUT_Avernus_C.lsf", "LevelCache/SYS_CC_I.lsf"];
        Assert.Equal(expectedEntries, package.Entries.Select(e => e.Name));
        var expected = new Dictionary<string, (int Bytes, int Nodes, int Attributes, int Roots)>
        {
            ["meta.lsf"] = (1516, 15, 39, 1),
            ["Globals.lsf"] = (2410069, 149174, 189357, 30),
            ["LevelCache/TUT_Avernus_C.lsf"] = (467459, 13981, 25867, 27),
            ["LevelCache/SYS_CC_I.lsf"] = (25350, 330, 780, 27),
        };
        foreach (var entry in package.Entries)
        {
            Assert.Equal(LarianCompressionMethod.Zstd, entry.Compression);
            var bytes = package.Extract(entry);
            Assert.Equal(entry.Size, bytes.Length);
            if (!expected.TryGetValue(entry.Name, out var counts))
            {
                continue;
            }

            var resource = LsfReader.Read(bytes);
            Assert.Equal(counts, (bytes.Length, resource.Nodes.Count, resource.Attributes.Count, resource.Roots.Count));
            if (entry.Name == "Globals.lsf")
            {
                var node = Assert.Single(resource.Nodes, n => n.Name == "NewAge");
                var blob = resource.Attributes[Assert.Single(node.Attributes)];
                Assert.Equal(LarianAttributeType.ScratchBuffer, blob.Type);
                Assert.Equal(2618936, blob.Data.Length);
                Assert.True(blob.Data.Span.StartsWith("LSMF"u8));
            }
            else if (entry.Name == "meta.lsf")
            {
                Assert.Equal("TUT_Avernus_C", AttributeDecoder.Read(Assert.Single(resource.Attributes, a => a.Name == "Level")).AsString());
                Assert.Equal(2, AttributeDecoder.Read(Assert.Single(resource.Attributes, a => a.Name == "Difficulty")).AsInt32());
                Assert.Equal(3, AttributeDecoder.Read(Assert.Single(resource.Attributes, a => a.Name == "OriginalPlatform")).AsInt32());
            }
        }
    }

    [Theory]
    [InlineData("analyticsUserData.lsf", 1, 1)]
    [InlineData("profile8.lsf", 17, 19)]
    [InlineData("config.lsf", 4, 10)]
    [InlineData("graphicSettings.lsx", 14, 29)]
    [InlineData("modsettings.lsx", 14, 26)]
    public void ProfileResourcesHaveKnownStructure(string name, int nodes, int attributes)
    {
        var file = SampleSaves.Load(TitleId).AllDiskFiles.First(f => f.FileName == name && !f.Directory.IsBackup);
        var bytes = file.ReadAllBytes();
        var resource = name.EndsWith(".lsf", StringComparison.Ordinal) ? LsfReader.Read(bytes) : LsxReader.Read(bytes);
        Assert.Equal(nodes, resource.Nodes.Count);
        Assert.Equal(attributes, resource.Attributes.Count);
    }

    [Fact]
    public void EveryClaimedCopyHasByteIdenticalNoOpEvenWhenReadOnly()
    {
        var plugin = new BaldursGate3Plugin();
        var files = SampleSaves.Load(TitleId).AllDiskFiles.Where(f => plugin.Probe(f).IsMatch).ToArray();
        Assert.Equal(12, files.Length);
        foreach (var file in files)
        {
            using var document = plugin.Open(file);
            Assert.False(document.CanEdit);
            var output = Assert.Single(document.Serialize());
            Assert.Equal(file.RelativePath, output.RelativePath);
            Assert.True(file.ReadAllBytes().AsSpan().SequenceEqual(output.Content.Span));
        }
    }

    [Fact]
    public void BackupTwinsMatchPrimaryContent()
    {
        var plugin = new BaldursGate3Plugin();
        var files = SampleSaves.Load(TitleId).AllDiskFiles.Where(f => plugin.Probe(f).IsMatch).ToArray();
        foreach (var primary in files.Where(f => !f.Directory.IsBackup))
        {
            var backup = Assert.Single(files, f => f.Directory.IsBackup && f.Directory.Name == primary.Directory.Name &&
                f.PathInDirectory == primary.PathInDirectory && f.Directory.Generation == primary.Directory.Generation);
            Assert.Equal(primary.ReadAllBytes(), backup.ReadAllBytes());
        }

        Assert.All(files, f => Assert.Equal(0, f.Directory.Generation)); // supplied archive has no additional snapshots
    }

    [Fact]
    public void HeaderDigestDoesNotMatchTheReferenceWriterAlgorithm()
    {
        var bytes = Save().ReadAllBytes();
        var package = LspkPackage.Read(bytes);
        var contents = package.Entries.Select(e => (e.Name, Bytes: package.Extract(e))).ToArray();
        foreach (var sorted in new[] { false, true })
        {
            // Legacy-format research only; this does not authenticate or protect user data.
#pragma warning disable CA5351
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
#pragma warning restore CA5351
            foreach (var entry in sorted ? contents.OrderBy(e => e.Name, StringComparer.Ordinal) : contents.AsEnumerable())
            {
                hash.AppendData(entry.Bytes);
            }

            var digest = hash.GetHashAndReset();
            for (var i = 0; i < digest.Length; i++)
            {
                digest[i]++;
            }

            Assert.False(digest.AsSpan().SequenceEqual(bytes.AsSpan(22, 16)),
                "The integrity limitation must be reassessed if the sample digest now matches.");
        }
    }

    private static ISaveFile Save() => SampleSaves.Load(TitleId).AllDiskFiles.First(f =>
        f.FileName == "AutoSave_0.lsv" && !f.Directory.IsBackup);
}
