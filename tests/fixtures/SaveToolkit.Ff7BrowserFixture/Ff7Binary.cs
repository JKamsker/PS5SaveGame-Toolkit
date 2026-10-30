using System.Buffers.Binary;
using System.IO.Compression;
using SaveToolkit.Plugins.FinalFantasy7.Tests;

namespace SaveToolkit.Testing.Ff7Browser;

/// <summary>Framework ZIP composition and in-memory variants over the plugin's pure test fixture.</summary>
public sealed record Ff7Binary(string TitleId, string GameplayName, string CommonName,
    byte[] Gameplay, byte[] Common, uint ItemType, uint MateriaType, uint SecondTierAp,
    IReadOnlyDictionary<string, int> FieldOffsets)
{
    private const string DirectoryName = "sdimg_E2E";
    public string GameplayPath => $"{TitleId}/{DirectoryName}/{GameplayName}";
    public string CommonPath => $"{TitleId}/{DirectoryName}/{CommonName}";
    public int NextRecordOffset => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(Gameplay.AsSpan(sizeof(uint))));

    public static Ff7Binary Create(string title)
    {
        var authored = Ff7AcceptanceFixtures.Create(title);
        return new(authored.TitleId, authored.GameplayName, authored.CommonName, authored.Gameplay.ToArray(), authored.Common.ToArray(),
            authored.ItemType, authored.MateriaType, authored.MateriaTargetAp, authored.FieldOffsets);
    }

    public Ff7Binary WithGameplay(byte[] bytes) => this with { Gameplay = bytes.ToArray() };
    public Ff7Binary WithIdentity(string title, string name) => this with { TitleId = title, GameplayName = name };

    public byte[] Zip()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(GameplayPath, Gameplay);
            Add(CommonPath, Common);
            void Add(string path, byte[] bytes)
            {
                using var entry = archive.CreateEntry(path).Open();
                entry.Write(bytes);
            }
        }
        return output.ToArray();
    }

    public uint Read(string id) => Read(Gameplay, id);
    public uint Read(byte[] bytes, string id) => ByteField(id) ? bytes[FieldOffsets[id]]
        : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(FieldOffsets[id]));

    public void Write(byte[] bytes, string id, uint value)
    {
        var offset = FieldOffsets[id];
        if (ByteField(id)) bytes[offset] = checked((byte)value);
        else BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }

    public Ff7Binary InventoryMatches(params int[] slots)
    {
        var bytes = Gameplay.ToArray();
        foreach (var id in FieldOffsets.Keys.Where(id => id.StartsWith("inventory/", StringComparison.Ordinal) && id.EndsWith("/type", StringComparison.Ordinal)))
            if (Read(id) == ItemType) Write(bytes, id, uint.MaxValue);
        foreach (var slot in slots)
        {
            Write(bytes, $"inventory/{slot}/type", ItemType);
            Write(bytes, $"inventory/{slot}/quantity", 1);
        }
        return WithGameplay(bytes);
    }

    public Ff7Binary OwnedVariant(string variant)
    {
        var bytes = Gameplay.ToArray();
        switch (variant)
        {
            case "moved":
                CopyDetail(0, 7);
                Write(bytes, "detail/0/instance", uint.MaxValue);
                Write(bytes, "materia/0/type", uint.MaxValue);
                Write(bytes, "materia/0/ap", 0);
                Write(bytes, "materia/0/level", 0);
                break;
            case "missing": Write(bytes, "detail/0/instance", uint.MaxValue); break;
            case "duplicate-detail": CopyDetail(0, 7); break;
            case "duplicate-inventory":
                foreach (var suffix in new[] { "instance", "type", "count" }) Write(bytes, "owned/7/" + suffix, Read("owned/0/" + suffix));
                break;
            case "invalid-count": Write(bytes, "owned/0/count", 2); break;
            case "wrong-type": Write(bytes, "materia/0/type", uint.MaxValue); break;
            default: throw new ArgumentException("Unknown authored owned-join variant.", nameof(variant));
        }
        return WithGameplay(bytes);

        void CopyDetail(int source, int destination)
        {
            foreach (var suffix in new[] { "instance", "owner" })
                Write(bytes, $"detail/{destination}/{suffix}", Read($"detail/{source}/{suffix}"));
            foreach (var suffix in new[] { "type", "ap", "level" })
                Write(bytes, $"materia/{destination}/{suffix}", Read($"materia/{source}/{suffix}"));
        }
    }

    private static bool ByteField(string id) => id.EndsWith("/level", StringComparison.Ordinal) || id.EndsWith("/owner", StringComparison.Ordinal);
}
