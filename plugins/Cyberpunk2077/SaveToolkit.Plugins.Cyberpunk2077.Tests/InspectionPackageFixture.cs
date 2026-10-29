using System.Text;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

/// <summary>Small independently encoded package fixtures; no sample bytes or production serializer.</summary>
internal sealed class InspectionPackageFixture
{
    private readonly List<string> _names = ["FixtureRoot"];
    private ushort Name(string name)
    {
        var index = _names.IndexOf(name);
        if (index < 0) { index = _names.Count; _names.Add(name); }
        return checked((ushort)index);
    }

    public byte[] Enum(string value) => BitConverter.GetBytes(Name(value));
    public byte[] Class(params (string Name, string Type, byte[] Data)[] fields)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, true);
        writer.Write(checked((ushort)fields.Length));
        var position = 2 + fields.Length * 8;
        foreach (var field in fields)
        {
            writer.Write(Name(field.Name)); writer.Write(Name(field.Type)); writer.Write(position);
            position += field.Data.Length;
        }
        foreach (var field in fields) writer.Write(field.Data);
        return output.ToArray();
    }

    public static byte[] Array(params byte[][] entries) => [.. BitConverter.GetBytes(entries.Length), .. entries.SelectMany(entry => entry)];

    public ScriptableReader Reader(byte[] root) => Reader(("FixtureRoot", root));
    public ScriptableReader Reader(params (string Type, byte[] Data)[] chunks) => new(Package(chunks));
    private byte[] Package((string Type, byte[] Data)[] chunks)
    {
        var types = chunks.Select(chunk => Name(chunk.Type)).ToArray();
        var nameData = _names.Count * 4;
        var descriptor = nameData + _names.Sum(name => name.Length + 1);
        var chunkData = descriptor + chunks.Length * 8;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, true);
        writer.Write(new byte[] { 4, 0, 6, 0 }); writer.Write(chunks.Length);
        writer.Write(0); writer.Write(nameData); writer.Write(descriptor); writer.Write(chunkData);
        writer.Write(0); // Scriptable root ID count.
        var offset = nameData;
        foreach (var name in _names)
        {
            writer.Write((uint)offset | ((uint)(name.Length + 1) << 24));
            offset += name.Length + 1;
        }
        foreach (var name in _names) { writer.Write(Encoding.ASCII.GetBytes(name)); writer.Write((byte)0); }
        offset = chunkData;
        for (var i = 0; i < chunks.Length; i++)
        {
            writer.Write((int)types[i]); writer.Write(offset);
            offset += chunks[i].Data.Length;
        }
        foreach (var chunk in chunks) writer.Write(chunk.Data);
        return output.ToArray();
    }
}
