using System.IO.Compression;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

/// <summary>Authored storage fixtures exercise schema guards, not native game values or checksum proof.</summary>
internal static class TitleFixture
{
    public const int InitialCount = 17;
    public const int OtherCount = 8;

    public static string TitleId(string title) => title switch
    {
        "jedi" => "PPSA02199", "sackboy" => "PPSA01288", _ => "UNKNOWN",
    };

    public static string CountName(string title) => title == "sackboy" ? "CollectabellsStored" : "Count";

    public static byte[] Scalars(string title, int count = InitialCount) => title switch
    {
        "generic" => GvasFixture.Build(writer => WriteScalars(writer, title, count)),
        "sackboy" => GvasFixture.Build(writer => WriteScalars(writer, title, count), minor: 25, patch: 1,
            packageVersion: 518, saveClass: "/Script/GingerBread.GingerbreadSaveUser"),
        "jedi" => Jedi(count),
        _ => throw new ArgumentException("Unknown authored title.", nameof(title)),
    };

    public static byte[] Settings() => GvasFixture.Build(writer =>
        GvasFixture.Property(writer, "HDRBrightness", "FloatProperty", GvasFixture.Body(body => body.Write(1.25f))),
        minor: 25, patch: 0, packageVersion: 518, customVersions: 56, saveClass: "/Script/SwGame.SwGameUserSettingsSaveObject");

    private static byte[] Jedi(int count)
    {
        byte[] plain = ActorProperties();
        return Jedi(Compress(plain), (uint)plain.Length, count);
    }

    public static byte[] Jedi(byte[] compressed, uint declared, int count = InitialCount) => GvasFixture.Build(writer =>
    {
        GvasFixture.Property(writer, "CompressedActorRecords", "ArrayProperty",
            GvasFixture.Body(body => { body.Write(compressed.Length); body.Write(compressed); }), "ByteProperty");
        GvasFixture.Property(writer, "UncompressedActorRecordsSize", "UInt32Property", GvasFixture.Body(body => body.Write(declared)));
        WriteScalars(writer, "jedi", count);
    }, minor: 25, patch: 0, packageVersion: 518, customVersions: 56, saveClass: "/Script/RsGameTechRT.RsSaveGameObject");

    public static byte[] Hogwarts(byte[] databaseFrames, byte[] exclusiveFrames) => GvasFixture.Build(writer =>
    {
        foreach (var (name, frames) in new[] { ("RawDatabaseImage", databaseFrames), ("RawExclusiveImage", exclusiveFrames) })
            GvasFixture.Property(writer, name, "ArrayProperty", GvasFixture.Body(body =>
            {
                body.Write(frames.Length);
                body.Write(frames);
            }), "ByteProperty");
    }, saveClass: HogwartsImage.SaveClass, customVersions: 72);

    public static byte[] ActorProperties() => GvasFixture.Body(writer =>
    {
        GvasFixture.Property(writer, "ActorRecords", "IntProperty", GvasFixture.Body(body => body.Write(42)));
        GvasFixture.String(writer, "None");
    });

    public static byte[] Compress(byte[] plain)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(plain);
        return output.ToArray();
    }

    private static void WriteScalars(BinaryWriter writer, string title, int count)
    {
        GvasFixture.Property(writer, CountName(title), "IntProperty", GvasFixture.Body(body => body.Write(count)));
        GvasFixture.Property(writer, "OtherCount", "IntProperty", GvasFixture.Body(body => body.Write(OtherCount)));
        if (title is "jedi" or "sackboy")
            GvasFixture.Property(writer, title == "jedi" ? "Crc32" : "Checksum", "UInt32Property", [0x78, 0x56, 0x34, 0x12]);
        GvasFixture.Property(writer, "Unrecognized", "FutureProperty", [0xFE, 0xED, 0xFA, 0xCE]);
    }
}
