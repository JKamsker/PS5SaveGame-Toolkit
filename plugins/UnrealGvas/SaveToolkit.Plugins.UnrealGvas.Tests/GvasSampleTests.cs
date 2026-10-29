using System.Diagnostics;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

[Collection(nameof(GvasProbeSampleCollection))]
public sealed class GvasSampleTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("PPSA01603", 34, 17)]
    [InlineData("PPSA02199", 6, 3)]
    public void AllClaimedSnapshotsAndBackupTwins_RoundTripExactly(string title, int expectedFiles, int expectedDistinct)
    {
        var plugin = new UnrealGvasPlugin();
        var files = SampleSaves.Load(title).AllDiskFiles.Where(f => f.FileName.StartsWith("ue4savegame", StringComparison.Ordinal)).ToArray();
        Assert.Equal(expectedFiles, files.Length);
        Assert.Equal(expectedDistinct, files.Select(f => f.Sha256).Distinct(StringComparer.Ordinal).Count());
        foreach (var file in files)
        {
            Assert.Equal(ProbeScore.Engine, plugin.Probe(file).Score);
            var stopwatch = Stopwatch.StartNew();
            using var document = plugin.Open(file);
            output.WriteLine($"Opening {file.RelativePath} took {stopwatch.Elapsed} (diagnostic only).");
            Assert.True(file.ReadAllBytes().AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span), file.RelativePath);
        }
    }

    [Theory]
    [InlineData("PPSA10695")]
    [InlineData("PPSA01802")]
    [InlineData("PPSA02343")]
    [InlineData("PPSA16064")]
    public void FilesWithoutGvas_AreNeverClaimedDespiteTheirUnrealNames(string title)
    {
        var files = SampleSaves.Load(title).AllDiskFiles.Where(f => f.FileName.StartsWith("ue4savegame", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(files);
        var plugin = new UnrealGvasPlugin();
        foreach (var file in files)
        {
            Assert.False(plugin.Probe(file).IsMatch);
            Assert.Throws<SaveFormatException>(() => plugin.Open(file));
        }
    }

    [Fact]
    public void HogwartsHl0010_HasVerifiedMetadataAndOodleDatabaseArrays()
    {
        var file = SampleSaves.Load("PPSA01603").AllDiskFiles.First(f => f.Directory.Name == "HL0010" && f.FileName == "ue4savegame.ps5.sav");
        var parsed = GvasFile.Parse(file.ReadAllBytes());
        Assert.Equal(72, parsed.Header.CustomVersionCount);
        Assert.Equal((ushort)27, parsed.Header.EngineMinor);
        Assert.Equal("/Script/PersistentData.PersistentGameData", parsed.Header.SaveClass);
        var properties = parsed.Properties.ToDictionary(n => n.Name);
        Assert.Equal((Int128)6, properties["Version"].Scalar!.Value);
        Assert.Equal("PS5", properties["Platform"].Scalar!.Value);
        Assert.Equal(true, properties["bDBC"].Scalar!.Value);
        foreach (var name in new[] { "RawDatabaseImage", "RawExclusiveImage" })
        {
            var blob = properties[name];
            Assert.Null(blob.Scalar);
            Assert.Empty(blob.Children);
            Assert.True(parsed.Original.Span.Slice(blob.Offset + 4, 8).SequenceEqual(new byte[] { 0xC1, 0x83, 0x2A, 0x9E, 0, 0, 0, 0 }));
            Assert.True(parsed.Original.Span.Slice(blob.Offset + 52, 2).SequenceEqual(new byte[] { 0x8C, 0x06 }));
        }
    }

    [Fact]
    public void JediSettings_HaveKnownValuesAndGameplayWritesWarnWhileIntegrityStaysReadOnly()
    {
        var files = SampleSaves.Load("PPSA02199").AllDiskFiles.Where(f => f.FileName == "ue4savegame.dpx.sav").ToArray();
        var settings = files.First(f => f.Directory.Name == "GameUserSettings");
        var parsed = GvasFile.Parse(settings.ReadAllBytes());
        Assert.Equal(56, parsed.Header.CustomVersionCount);
        Assert.Equal("/Script/SwGame.SwGameUserSettingsSaveObject", parsed.Header.SaveClass);
        Assert.Equal((double)1.1f, parsed.Properties.Single(n => n.Name == "HDRBrightness").Scalar!.Value);
        using var settingsDocument = new UnrealGvasPlugin().Open(settings);
        Assert.True(settingsDocument.CanEdit);
        foreach (var file in files.Where(f => f.Directory.Name is "SaveGame00" or "Backup00"))
        {
            using var document = new UnrealGvasPlugin().Open(file);
            Assert.True(document.CanEdit);
            var fields = GvasFormatTests.Expand(document);
            Assert.NotEmpty(fields);
            Assert.Contains(fields, f => f.Label.Contains("Crc32", StringComparison.Ordinal) && f.Spec.ReadOnly);
            Assert.Contains(fields, f => !f.Spec.ReadOnly);
        }
    }
}
