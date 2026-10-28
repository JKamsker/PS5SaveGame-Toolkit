using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Altered C# adaptation of the pinned zlib datalib.py layouts; see Notices and README.</summary>
// Copyright (c) 2020-2022 CJ Kucera, Abram Hindle. Retained zlib notices: Notices/*-COPYING.txt.
internal static class SerialWrites
{
    public const string Attribution = "Sources: CJ Kucera / Abram Hindle, apocalyptech bl3-cli-saveedit d459b28 and ttwl-cli-saveedit 1fde90d " +
        "(Zlib, retrieved 2026-10-28; altered C# implementation). Source links and retained notices are in Sources and the Gearbox README. ";
    public const string Warning = "Not validated on console. Based on pinned apocalyptech schemas/serial layouts and PS5 sample round trips. " +
        "Known item CRC is repaired; no additional wrapper checksum is known. The game may reject, clamp or remove edited gear. " +
        "Unknown gameplay dependencies are unchanged; validate on a backed-up spare slot (owner tracking #31). " + Attribution;

    public static int Maximum(bool wonderlands, string property) => property switch
    {
        "level" => wonderlands ? 40 : 72,
        "rerolls" => byte.MaxValue,
        "chaos" when wonderlands => 4,
        _ => throw new SaveFormatException("Unsupported serial property."),
    };

    public static int? Value(SerialInfo info, string property) => property switch
    { "level" => info.Level, "rerolls" => info.Rerolls, "chaos" => info.Chaos, _ => null };

    public static byte[] Set(byte[] serial, bool wonderlands, string property, int value)
    {
        if (value < (property == "level" ? 1 : 0) || value > Maximum(wonderlands, property))
            throw new SaveFormatException("The serial value is outside the supported editor policy.");
        var info = new SerialReader(wonderlands).Read(serial, out var envelope);
        var offset = property switch { "level" => info.LevelOffset, "rerolls" => info.RerollOffset, "chaos" => info.ChaosOffset, _ => null };
        // The header's exact level bits can be patched without interpreting or rebuilding an unknown tail.
        if (!info.ChecksumValid || info.Balance?.Path is null || info.Inventory?.Path is null || info.Manufacturer?.Path is null || offset is null)
            throw new SaveFormatException("This serial property has no safely located bit range; original bytes are preserved.");
        var edited = envelope!.Patch(offset.Value, property == "rerolls" ? 8 : 7, value);
        var check = new SerialReader(wonderlands).Read(edited);
        if (!check.ChecksumValid || Value(check, property) != value) throw new SaveFormatException("Edited serial failed validation.");
        return edited;
    }
}
