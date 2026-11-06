using System.Text;
using System.Text.Json;
using SaveToolkit.Plugins.BaldursGate3;
using SaveToolkit.Plugins.BaldursGate3.Tests;
using SaveToolkit.Plugins.Witcher3;
using SaveToolkit.Plugins.Witcher3.Tests;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Testing.CodecBrowser;

/// <summary>
/// Writes authored saves whose edits are re-encoded with LZ4 HC (the path that trapped the browser
/// runtime), each with the bytes the native plugin writes after the same edit, so the browser e2e
/// suite can compare its export byte for byte. Prints one <c>CODEC_FIXTURE:</c> JSON line.
/// </summary>
internal static class FixtureProgram
{
    public static void Main()
    {
        var fixtures = new[]
        {
            // Witcher 3 re-encodes the edited chunk at LZ4 L12_MAX.
            Create("witcher3", "PPSA04018/sdimg_E2E/Manual.sav", SyntheticPlayerSave.Create(), new Witcher3Plugin(),
                "player/level", 2),
            // BG3 rewrites the package entry and file table as LZ4 L10_OPT blocks.
            Create("bg3", "PPSA18463/sdimg_E2E/AutoSave_0.lsv", Bg3Package(), new BaldursGate3Plugin(), "party/0/Level", 12),
        };
        Console.WriteLine("CODEC_FIXTURE:" + JsonSerializer.Serialize(fixtures));
    }

    private static byte[] Bg3Package()
    {
        const string json = "{\"Active Party\":{\"Characters\":[{\"Origin\":\"Generic\",\"Level\":1," +
            "\"Experience Points (Current level)\":30,\"Experience Points (Total)\":30}]}}";
        return PackageFixture.Create(method: 2, payloads: [("SaveInfo.json", Encoding.UTF8.GetBytes(json)), ("unknown.bin", [0xde, 0xad])]);
    }

    private static object Create(string plugin, string path, byte[] original, IGamePlugin game, string valueId, long value)
    {
        var parts = path.Split('/');
        using var document = game.Open(MemorySaveSet.SingleFile(parts[^1], original, parts[0], parts[1]));
        _ = document.BuildView();
        var applied = document.ApplyEdits([new(valueId, FieldValue.Of(value))]);
        if (!applied.Succeeded)
            throw new InvalidOperationException(string.Join("; ", applied.Errors.Select(e => $"{e.ValueId}: {e.Message}")));
        var edited = document.Serialize().Single().Content.ToArray();
        if (edited.AsSpan().SequenceEqual(original))
            throw new InvalidOperationException($"The {plugin} edit did not change the save, so it would not exercise the encoder.");
        return new
        {
            plugin, path, valueId, value = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            original = Convert.ToBase64String(original), edited = Convert.ToBase64String(edited),
        };
    }
}
