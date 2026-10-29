using System.Text;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

/// <summary>Sample selection, synthetic containers and save wrappers shared by the plugin tests.</summary>
internal static class GearboxTestData
{
    public static DiskSaveFile PrimaryCharacter(string title) => SampleSaves.Load(title).AllDiskFiles
        .First(f => f.FileName == "1" && !f.Directory.IsBackup);

    public static ISaveFile File(string title, byte[] bytes, string name = "1") =>
        new MemorySaveSet().With(title, "TEST", new Dictionary<string, byte[]> { [name] = bytes }).Directories[0].Files[0];

    public static ISaveFile Overlay(ISaveFile file, ReadOnlyMemory<byte> bytes) =>
        ((ISaveSet)new OverlaySaveSet(file.Directory.SaveSet,
            new Dictionary<string, ReadOnlyMemory<byte>> { [file.RelativePath] = bytes })).FindFile(file.RelativePath)!;

    public static ProtoMessage AddMessage(ProtoMessage parent, string name)
    {
        var spec = parent.Schema.Spec(parent.Type, name);
        var child = new ProtoMessage(parent.Schema, spec.Type, []);
        parent.Add(new ProtoField(parent.Schema.Tag(parent.Type, name), 2, spec, [], Array.Empty<byte>(), child));
        return child;
    }

    public static byte[] CreateContainer(bool wonderlands, bool profile, bool unicode = false, byte[]? tail = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("GVAS"u8);
        writer.Write(2);
        writer.Write(516);
        writer.Write((ushort)4);
        writer.Write((ushort)20);
        writer.Write((ushort)3);
        writer.Write(1234);
        WriteString(writer, wonderlands ? "DAFFODIL-test" : "OAK-test", unicode);
        writer.Write(3);
        writer.Write(1);
        writer.Write(Enumerable.Range(0, 16).Select(n => (byte)n).ToArray());
        writer.Write(99);
        WriteString(writer, (wonderlands, profile) switch
        {
            (false, false) => "OakSaveGame", (false, true) => "BP_DefaultOakProfile_C",
            (true, false) => "BPSaveGame_Default_C", (true, true) => "OakProfile",
        }, unicode);
        byte[] plain = profile ? [0x08, 0x01] : [0x22, 0x06, 0x0A, 0x04, 0x74, 0x65, 0x73, 0x74, 0x38, 0x00];
        var ciphertext = BogoCrypt.Encode(plain, profile);
        writer.Write(ciphertext.Length);
        writer.Write(ciphertext);
        writer.Write(tail ?? []);
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string text, bool unicode)
    {
        writer.Write((text.Length + 1) * (unicode ? -1 : 1));
        writer.Write((unicode ? Encoding.Unicode : Encoding.UTF8).GetBytes(text + '\0'));
    }
}
