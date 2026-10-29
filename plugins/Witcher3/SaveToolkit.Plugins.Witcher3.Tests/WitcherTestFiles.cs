using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

internal static class WitcherTestFiles
{
    public static ISaveDocument Open(byte[] bytes) =>
        new Witcher3Plugin().Open(MemorySaveSet.SingleFile("Manual.sav", bytes, "PPSA04018"));

    public static DiskSaveFile Import(byte[] bytes, string? outputDirectory = null) => Assert.Single(SaveSetImporter.Import(
        [new ImportSource("PPSA04018/sdimg_TEST/Manual.sav", () => new MemoryStream(bytes))],
        outputDirectory ?? Path.Combine(TestScratch.ProcessFolder, Guid.NewGuid().ToString("N"))).AllDiskFiles);
}
