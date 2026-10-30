namespace SaveToolkit.Core.Presets;

/// <summary>Finite request limits derived from the supported transfer domains, independent from package bytes.</summary>
public static class PresetTransferLimits
{
    public const int MaxRecords = 256;
    public const int MaxPresetIdLength = 128;
    // Each UTF-16 unit can use six JSON bytes (\uXXXX), including escaped surrogate pairs.
    // 256 bytes per choice cover escaped property names/decision, index and punctuation;
    // 2 KiB cover the escaped 32-character server token, outer envelope and formatting.
    public const int MaxControlBytes = 2048 + MaxRecords * (MaxPresetIdLength * 6 + 256);
}
