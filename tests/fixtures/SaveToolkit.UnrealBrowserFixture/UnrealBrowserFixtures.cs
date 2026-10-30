using System.IO.Compression;
using SaveToolkit.Plugins.UnrealGvas.Tests;

namespace SaveToolkit.Testing.UnrealBrowser;

/// <summary>Authored GVAS bytes, not game captures or evidence of console compatibility.</summary>
public static class UnrealBrowserFixtures
{
    public const string JediTitle = "PPSA02199";
    public const string SackboyTitle = "PPSA01288";
    public const float OriginalBrightness = 1.25f;
    public const float EditedBrightness = 1.75f;
    public const int WideRowCount = 200;
    public const string WideTail = "unique-synthetic-tail";
    public static string WideValue => new string('雪', 4096) + WideTail;

    public static IReadOnlyDictionary<string, byte[]> Files() => new Dictionary<string, byte[]>
    {
        ["settings.sav"] = Settings(),
        ["settings-edited.sav"] = Settings(EditedBrightness),
        ["wide.sav"] = WideSettings(),
        ["sackboy.sav"] = Sackboy(),
        ["actors.sav"] = Actors(),
    };

    // Same header/title gates as TitleFixture.Settings and GvasFieldReferenceTests.
    public static byte[] Settings(float brightness = OriginalBrightness) => SettingsFile(writer =>
        GvasFixture.Property(writer, "HDRBrightness", "FloatProperty", GvasFixture.Body(body => body.Write(brightness))));

    public static byte[] WideSettings() => SettingsFile(writer =>
    {
        for (var index = 0; index < WideRowCount; index++)
            GvasFixture.Property(writer, "Wide" + index, "StrProperty",
                GvasFixture.Body(body => GvasFixture.String(body, WideValue, wide: true)));
    });

    public static byte[] Sackboy() => GvasFixture.Build(writer =>
    {
        GvasFixture.Property(writer, "CollectabellSaveData", "StructProperty", GvasFixture.Body(body =>
        {
            GvasFixture.Property(body, "CollectabellsStored", "IntProperty", GvasFixture.Body(value => value.Write(17)));
            GvasFixture.String(body, "None");
        }), "CollectabellSaveData");
        GvasFixture.Property(writer, "OpaqueContainer", "FutureProperty", [0xFE, 0xED, 0xFA, 0xCE]);
        GvasFixture.Property(writer, "Checksum", "UInt32Property", [0x78, 0x56, 0x34, 0x12]);
    }, minor: 25, patch: 1, packageVersion: 518, saveClass: "/Script/GingerBread.GingerbreadSaveUser");

    public static byte[] Actors()
    {
        var plain = GvasFixture.Body(writer =>
        {
            GvasFixture.Property(writer, "ActorRecords", "IntProperty", GvasFixture.Body(body => body.Write(42)));
            GvasFixture.String(writer, "None");
        });
        var compressed = GvasFixture.Compress(plain, CompressionLevel.SmallestSize);
        return GvasFixture.Build(writer =>
        {
            GvasFixture.Property(writer, "CompressedActorRecords", "ArrayProperty", GvasFixture.Body(body =>
            {
                body.Write(compressed.Length);
                body.Write(compressed);
            }), "ByteProperty");
            GvasFixture.Property(writer, "UncompressedActorRecordsSize", "UInt32Property", GvasFixture.Body(body => body.Write((uint)plain.Length)));
            GvasFixture.Property(writer, "Crc32", "UInt32Property", [0x78, 0x56, 0x34, 0x12]);
        }, minor: 25, patch: 0, packageVersion: 518, customVersions: 56, saveClass: "/Script/RsGameTechRT.RsSaveGameObject");
    }

    private static byte[] SettingsFile(Action<BinaryWriter> properties) => GvasFixture.Build(properties,
        minor: 25, patch: 0, packageVersion: 518, customVersions: 56, saveClass: "/Script/SwGame.SwGameUserSettingsSaveObject");
}
