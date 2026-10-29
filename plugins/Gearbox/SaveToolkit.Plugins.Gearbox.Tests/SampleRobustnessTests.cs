using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class SampleRobustnessTests
{
    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void EverySample_RejectsIncompleteHeadersAndInvalidReencryptedPayloads(string title)
    {
        var plugin = new GearboxPlugin();
        var files = SampleSaves.Load(title).AllDiskFiles.Where(f => plugin.Probe(f).IsMatch).ToArray();
        Assert.Equal(12, files.Length);
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            var container = GearboxContainer.Parse(bytes, title, file.FileName == "profile");
            for (var length = 0; length < container.PayloadOffset; length++)
            {
                var truncated = bytes[..length];
                Assert.Throws<SaveFormatException>(() => plugin.Open(SampleFormatTests.Overlay(file, truncated)));
            }
            var plain = BogoCrypt.Decode(bytes.AsSpan(container.PayloadOffset, container.PayloadLength), container.Profile);
            // Keep all expected game records and repair outer framing/encryption. Rejection must
            // come from the malformed appended wire record, not an unrelated header/mask error.
            foreach (var suffix in new byte[][] { [0x9A, 0x06, 0xFF, 0xFF, 0xFF, 0xFF, 7],
                [0x9B, 0x06], [0x9A, 0x06, 0x81, 0x80, 0x80, 0x80, 0x10, 0xAA] })
            {
                var damaged = WithPayload(container, [.. plain, .. suffix]);
                Assert.Throws<SaveFormatException>(() => plugin.Open(SampleFormatTests.Overlay(file, damaged)));
            }
            using var document = plugin.Open(file);
            Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
        }
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void TruncatedSerialInEverySample_PreservesFileAndUnrelatedViews(string title)
    {
        var plugin = new GearboxPlugin();
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(f => plugin.Probe(f).IsMatch))
        {
            var container = GearboxContainer.Parse(file.ReadAllBytes(), title, file.FileName == "profile");
            var root = container.Decode();
            var list = container.Profile ? GearboxInventory.Bank : GearboxInventory.Backpack;
            var entry = root.Last(list);
            if (entry is null)
            {
                var spec = root.Schema.Spec(root.Type, list);
                if (root.Schema.IsMessage(spec.Type)) _ = WriteAtomicityTests.AddMessage(root, list);
                else root.Add(new ProtoField(root.Schema.Tag(root.Type, list), 2, spec, [], Array.Empty<byte>(), null));
                entry = root.Last(list)!;
            }
            if (entry.Message is { } item)
            {
                var spec = root.Schema.Spec(item.Type, "item_serial_number");
                if (item.Last("item_serial_number") is { } serial) serial.Set(new byte[] { 4, 0, 0 });
                else item.Add(new ProtoField(root.Schema.Tag(item.Type, "item_serial_number"), 2, spec, [], new byte[] { 4, 0, 0 }, null));
            }
            else entry.Set(new byte[] { 4, 0, 0 });
            var damaged = container.Encode(root);
            using var document = (GearboxDocument)plugin.Open(SampleFormatTests.Overlay(file, damaged));
            _ = document.BuildView();
            var corrupt = document.Inventory.Entries.Single(e => e.Serial.AsSpan().SequenceEqual(new byte[] { 4, 0, 0 }));
            Assert.False(document.Inventory.Info(corrupt.Id).Complete);
            var page = document.Query(list, new QueryableCollectionQuery { Offset = corrupt.Index, Limit = 1 });
            var row = Assert.Single(page.Rows);
            _ = document.ExpandNode(((SectionNode)row.Cells["detail"]).LazyId!);
            Assert.Equal(damaged, document.Serialize().Single().Content.ToArray());
        }
    }

    private static byte[] WithPayload(GearboxContainer container, byte[] plain)
    {
        var ciphertext = BogoCrypt.Encode(plain, container.Profile);
        byte[] result = [.. container.Original[..container.PayloadOffset], .. ciphertext,
            .. container.Original[(container.PayloadOffset + container.PayloadLength)..]];
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(container.PayloadOffset - sizeof(int)), ciphertext.Length);
        return result;
    }
}
