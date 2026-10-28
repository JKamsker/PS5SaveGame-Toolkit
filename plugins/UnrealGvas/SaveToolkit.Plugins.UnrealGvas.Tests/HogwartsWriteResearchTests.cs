using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsWriteResearchTests
{
    [Fact]
    public void Hl0010_SerializedSqliteAndStoredFramesRebuildGvasWithoutEnablingWrites()
    {
        var sample = SampleSaves.Load("PPSA01603").AllDiskFiles.First(file =>
            file.Directory.Name == "HL0010" && file.FileName == "ue4savegame.ps5.sav");
        byte[] original = sample.ReadAllBytes();
        byte[] rebuilt = original;
        var expectedImages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in new[] { "RawDatabaseImage", "RawExclusiveImage" })
        {
            var gvas = GvasFile.Parse(original);
            byte[] image = HogwartsImage.ReadArray(gvas, Assert.Single(gvas.Properties, node => node.Name == name));
            using var database = SqliteImage.Open(image);
            byte[] serialized = database.Serialize(); // Includes an independent native integrity_check.
            Assert.Equal(image, serialized);
            expectedImages.Add(name, serialized);
            rebuilt = HogwartsWriteProbe.Replace(rebuilt, name, HogwartsImage.EncodeStored(serialized));
        }
        Assert.True(rebuilt.Length > original.Length);
        var parsed = GvasFile.Parse(rebuilt);
        foreach (var (name, expected) in expectedImages)
        {
            var node = Assert.Single(parsed.Properties, node => node.Name == name);
            byte[] independentlyDecoded = HogwartsWriteProbe.IndependentlyDecode(parsed, node);
            Assert.Equal(expected, independentlyDecoded);
            using var reopened = SqliteImage.Open(independentlyDecoded);
            Assert.Equal(expected, reopened.Serialize());
        }
        // GVAS writer research stays in this test. Actual document serialization retains original frames.
        using var document = new UnrealGvasPlugin().Open(sample);
        Assert.True(document.CanEdit);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "hogwarts-write-size.txt"), $"original={original.Length}; stored={rebuilt.Length}");
    }
}
