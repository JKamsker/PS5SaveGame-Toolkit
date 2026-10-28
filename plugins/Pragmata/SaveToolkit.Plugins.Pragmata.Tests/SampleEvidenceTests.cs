using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Hashing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class SampleEvidenceTests
{
    [Fact]
    public void AllTwelveFilesMatchVerifiedMapAndConsumePayloadExactly()
    {
        using var map = LoadMap();
        var files = Files().ToArray();
        Assert.Equal(12, files.Length);
        var blocks = 0;
        var classes = 0;
        foreach (var expected in map.RootElement.GetProperty("files").EnumerateArray())
        {
            var file = files.Single(f => f.FileName == expected.GetProperty("file").GetString());
            var bytes = file.ReadAllBytes();
            Assert.Equal(expected.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            var save = new DsssContainer(bytes);
            var reader = new TypedReader(save.Plaintext);
            var roots = reader.Parse();
            Assert.Equal(save.Plaintext.Length, reader.Position);
            Assert.Equal(expected.GetProperty("plaintext_bytes").GetInt32(), save.Plaintext.Length);
            Assert.Equal(expected.GetProperty("blocks").GetInt32(), save.BlockCount);
            Assert.Equal(expected.GetProperty("class_count").GetInt32(), reader.Classes.Count);
            Assert.Equal(expected.GetProperty("scalar_count").GetInt32(), reader.ScalarCount);
            Assert.Equal(expected.GetProperty("root_count").GetInt32(), roots.Count);
            Assert.Equal(bytes, save.Rebuild(save.Plaintext));
            var actualClasses = reader.Classes.ToDictionary(c => c.Path);
            foreach (var root in expected.GetProperty("roots").EnumerateArray())
                CheckClass(root, actualClasses, save.Plaintext);
            if (expected.TryGetProperty("gameplay_sections", out var sections))
                foreach (var section in sections.EnumerateArray()) CheckClass(section, actualClasses, save.Plaintext);
            CheckInventory(expected, save.Plaintext);
            blocks += save.BlockCount;
            classes += reader.Classes.Count;
        }
        Assert.Equal(503, blocks);
        Assert.Equal(519872, classes);
    }

    [Fact]
    public void EveryFileRejectsCiphertextDamageEvenAfterOuterHashIsRepaired()
    {
        foreach (var file in Files())
        {
            var bytes = file.ReadAllBytes();
            bytes[DsssContainer.HeaderSize + DsssContainer.MetadataSize] ^= 1;
            RepairOuterHash(bytes);
            var error = Assert.Throws<SaveFormatException>(() => new DsssContainer(bytes));
            Assert.Contains("CityHash64", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EmbeddedAuthenticationDamageIsRejected()
    {
        var bytes = Files().First().ReadAllBytes();
        bytes[DsssContainer.HeaderSize] ^= 1;
        RepairOuterHash(bytes);
        Assert.Throws<SaveFormatException>(() => new DsssContainer(bytes));
    }

    private static void CheckClass(JsonElement expected, Dictionary<string, SaveNode> classes, byte[] plaintext)
    {
        var actual = classes[expected.GetProperty("path").GetString()!];
        Assert.Equal(expected.GetProperty("class").GetString(), actual.ClassHash.ToString("x8", CultureInfo.InvariantCulture));
        Assert.Equal(expected.GetProperty("offset").GetInt32(), actual.Offset);
        foreach (var field in expected.GetProperty("fields").EnumerateArray())
        {
            var hash = uint.Parse(field.GetProperty("hash").GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var node = actual.Find(hash)!;
            Assert.Equal(field.GetProperty("path").GetString(), node.Path);
            Assert.Equal(field.GetProperty("type").GetInt32(), node.Type);
            if (field.TryGetProperty("value_offset", out var offset)) Assert.Equal(offset.GetInt32(), node.Offset);
            if (field.TryGetProperty("size", out var size)) Assert.Equal(size.GetInt32(), node.Size);
            if (field.TryGetProperty("value_shape", out var shape))
            {
                if (shape.TryGetProperty("count", out var count)) Assert.Equal(count.GetInt32(), node.Children.Count);
                if (shape.TryGetProperty("class", out var classHash))
                    Assert.Equal(classHash.GetString(), node.ClassHash.ToString("x8", CultureInfo.InvariantCulture));
            }
            if (!field.TryGetProperty("value", out var value) || !node.IsPrimitive) continue;
            var read = PrimitiveCodec.Read(node, plaintext);
            if (node.Type == 2) Assert.Equal(value.GetBoolean(), read.AsBool());
            else if (node.Type is 11 or 12) Assert.Equal(value.GetDouble(), read.AsDouble());
            else Assert.Equal(Int128.Parse(value.GetRawText(), CultureInfo.InvariantCulture), read.AsInteger());
        }
    }

    private static void CheckInventory(JsonElement expected, byte[] plaintext)
    {
        if (!expected.TryGetProperty("inventory", out var inventory)) return;
        var layout = new SaveLayout(plaintext);
        var entries = inventory.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(entries.Length, layout.Items.Count);
        for (var i = 0; i < entries.Length; i++)
        {
            foreach (var fieldName in new[] { "id_field", "quantity_field" })
            {
                var field = entries[i].GetProperty(fieldName);
                var hash = uint.Parse(field.GetProperty("hash").GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var node = layout.Items[i].Find(hash)!;
                Assert.Equal(field.GetProperty("value_offset").GetInt32(), node.Offset);
                Assert.Equal(field.GetProperty("path").GetString(), node.Path);
                Assert.Equal(Int128.Parse(field.GetProperty("value").GetRawText(), CultureInfo.InvariantCulture), PrimitiveCodec.Read(node, plaintext).AsInteger());
            }
        }
        var playtime = PrimitiveCodec.Read(layout.Header!.Find(KnownHashes.Playtime)!, plaintext).AsInteger();
        Assert.Equal(playtime, PrimitiveCodec.Read(layout.Timers[0], plaintext).AsInteger() -
            PrimitiveCodec.Read(layout.Timers[1], plaintext).AsInteger() - PrimitiveCodec.Read(layout.Timers[2], plaintext).AsInteger());
    }

    internal static IEnumerable<ISaveFile> Files() => SampleSaves.Load("PPSA02530").AllDiskFiles
        .Where(f => new PragmataPlugin().Probe(f).IsMatch).DistinctBy(f => f.Sha256);

    internal static JsonDocument LoadMap()
    {
        using var stream = typeof(SampleEvidenceTests).Assembly.GetManifestResourceStream("Pragmata.VerifiedSaveMap.json")!;
        return JsonDocument.Parse(stream);
    }

    internal static void RepairOuterHash(byte[] bytes) => BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(bytes.Length - 4), Murmur3.Hash(bytes.AsSpan(0, bytes.Length - 4)));
}
