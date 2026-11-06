using System.Text;
using System.Text.Json;
using K4os.Compression.LZ4;
using SaveToolkit.Plugins.BaldursGate3;
using SaveToolkit.Plugins.BaldursGate3.Tests;
using SaveToolkit.Plugins.Cyberpunk2077;
using SaveToolkit.Plugins.Cyberpunk2077.Tests;
using SaveToolkit.Plugins.Witcher3;
using SaveToolkit.Plugins.Witcher3.Tests;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Testing.CodecBrowser;

/// <summary>
/// Writes authored saves whose edits are re-encoded with LZ4 (HC levels trapped the browser runtime; the
/// fast level wrote K4os' 32-bit bytes there), each with the bytes the native plugin writes after the
/// same edit, so the browser e2e suite can compare its export byte for byte. Prints one
/// <c>CODEC_FIXTURE:</c> JSON line.
/// </summary>
internal static class FixtureProgram
{
    public static void Main()
    {
        if (!Environment.Is64BitProcess || LZ4Codec.Enforce32)
            throw new InvalidOperationException("The native reference bytes must come from K4os' 64-bit engine.");
        var fixtures = new[]
        {
            // Witcher 3 re-encodes the edited chunk at LZ4 L12_MAX.
            Create("witcher3", "PPSA04018/sdimg_E2E/Manual.sav", SyntheticPlayerSave.Create(), new Witcher3Plugin(),
                "player/level", 2),
            // BG3 rewrites the package entry and file table as LZ4 L10_OPT blocks.
            Create("bg3", "PPSA18463/sdimg_E2E/AutoSave_0.lsv", Bg3Package(method: 2, padding: 0), new BaldursGate3Plugin(),
                "party/0/Level", 12),
            // BG3 fast-level (0x12) entry over 64 KiB: K4os' 32-bit engine (the browser's) writes other bytes.
            Create("bg3-fast", "PPSA18463/sdimg_E2EFAST/AutoSave_1.lsv", Bg3Package(method: 0x12, padding: 120_000),
                new BaldursGate3Plugin(), "party/0/Level", 7),
            // Cyberpunk re-encodes the edited 256 KiB chunk at K4os' default (fast) level.
            Create("cyberpunk2077", "PPSA04027/sdimg_E2E/sav.dat", SyntheticFactsSave.Create(), new Cyberpunk2077Plugin(),
                SyntheticFactsSave.FirstFactValueId, 0, document => _ = SyntheticFactsSave.FirstFactField(document)),
        };
        RequireThirtyTwoBitDifference(SyntheticFactsSave.Payload()[..SyntheticFactsSave.FirstChunkSize], "Cyberpunk");
        Console.WriteLine("CODEC_FIXTURE:" + JsonSerializer.Serialize(fixtures));
    }

    private static byte[] Bg3Package(byte method, int padding)
    {
        // The padding is ordinary save metadata text, so the edited entry is a well-compressible block.
        var notes = padding == 0 ? "" : ",\"Notes\":\"" + string.Concat(Enumerable.Range(0, padding / 24)
            .Select(i => $"Camp {i % 37:D2} Long Rest; ")) + "\"";
        var json = "{\"Active Party\":{\"Characters\":[{\"Origin\":\"Generic\",\"Level\":1," +
            "\"Experience Points (Current level)\":30,\"Experience Points (Total)\":30}]}" + notes + "}";
        RequireThirtyTwoBitDifference(Encoding.UTF8.GetBytes(json), padding == 0 ? null : "BG3 fast");
        return PackageFixture.Create(method: method, payloads: [("SaveInfo.json", Encoding.UTF8.GetBytes(json)), ("unknown.bin", [0xde, 0xad])]);
    }

    /// <summary>Proves the fixture exercises the browser difference: K4os' two fast engines disagree on it.</summary>
    private static void RequireThirtyTwoBitDifference(byte[] content, string? name)
    {
        if (name is null) return;
        var native = Encode(content);
        LZ4Codec.Enforce32 = true;
        try
        {
            if (native.AsSpan().SequenceEqual(Encode(content)))
                throw new InvalidOperationException($"The {name} fixture encodes alike on K4os' 32- and 64-bit engines.");
        }
        finally
        {
            LZ4Codec.Enforce32 = false;
        }

        static byte[] Encode(byte[] content)
        {
            var buffer = new byte[LZ4Codec.MaximumOutputSize(content.Length)];
            return buffer[..LZ4Codec.Encode(content, buffer)];
        }
    }

    private static object Create(string plugin, string path, byte[] original, IGamePlugin game, string valueId, long value,
        Action<ISaveDocument>? prepare = null)
    {
        var parts = path.Split('/');
        using var document = game.Open(MemorySaveSet.SingleFile(parts[^1], original, parts[0], parts[1]));
        _ = document.BuildView();
        prepare?.Invoke(document);
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
