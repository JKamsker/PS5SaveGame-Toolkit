using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

/// <summary>The three authored Tsushima schemas supported by the caption acceptance fixture.</summary>
public enum TsushimaCaptionFixtureVersion
{
    V47 = 47,
    V48 = 48,
    V49 = 49,
}

/// <summary>Pure, sample-free fixture bridge for framework and browser acceptance consumers.</summary>
/// <remarks>
/// Reuses the independent test writers, never production serialization. PPSA02225 is the supported
/// title identity; the slot, gameplay records and caption are invented, not a regional/native save.
/// No Yotei, arbitrary schema, path or caption parameters are accepted.
/// </remarks>
public static class SuckerPunchCaptionAcceptanceFixtures
{
    /// <summary>Creates a fresh complete source set with a plain caption and room for multibyte edits.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The schema is not V47, V48 or V49.</exception>
    public static SuckerPunchCaptionAcceptanceFixture Create(
        TsushimaCaptionFixtureVersion version = TsushimaCaptionFixtureVersion.V49)
    {
        if (!Enum.IsDefined(version)) throw new ArgumentOutOfRangeException(nameof(version));
        var save = new SyntheticSave(yotei: false);
        const int versionWordOffset = 3 * sizeof(uint);
        save.Write(versionWordOffset, (uint)version);
        save.Repair();
        var primary = CaptionTestData.File(CaptionTestData.Sfo(), save);
        var sfo = primary.Directory.FindAnyFile(CaptionTestData.SfoPath)!;
        var companion = primary.Directory.Files.Single(file => file.RelativePath != primary.RelativePath);
        return new(version, primary, sfo, companion);
    }
}

/// <summary>Read-only source bytes and exact save-set-relative paths for one invented Tsushima slot.</summary>
/// <remarks>
/// Each factory call owns independent buffers. Consumers must call ToArray() before mutations and
/// import all three files at their exact paths. This is managed byte evidence, not console acceptance.
/// The SFO includes nonzero reserved capacity, an unknown entry and a trailing sentinel.
/// </remarks>
public sealed class SuckerPunchCaptionAcceptanceFixture
{
    internal SuckerPunchCaptionAcceptanceFixture(TsushimaCaptionFixtureVersion version,
        ISaveFile primary, ISaveFile sfo, ISaveFile companion)
    {
        Version = version;
        TitleId = primary.Directory.TitleId;
        PrimaryRelativePath = primary.RelativePath;
        GameplayBytes = primary.ReadAllBytes();
        SfoRelativePath = sfo.RelativePath;
        SfoBytes = sfo.ReadAllBytes();
        CompanionRelativePath = companion.RelativePath;
        CompanionBytes = companion.ReadAllBytes();
    }

    public TsushimaCaptionFixtureVersion Version { get; }
    public string TitleId { get; }
    public string PrimaryRelativePath { get; }
    public ReadOnlyMemory<byte> GameplayBytes { get; }
    public string SfoRelativePath { get; }
    public ReadOnlyMemory<byte> SfoBytes { get; }
    public string CompanionRelativePath { get; }
    public ReadOnlyMemory<byte> CompanionBytes { get; }
    public string Caption => CaptionTestData.Original;
    /// <summary>UTF-8 payload limit, excluding the storage NUL (32-byte entry capacity).</summary>
    public int MaxUtf8Bytes => CaptionTestData.DefaultCapacity - 1;
}
