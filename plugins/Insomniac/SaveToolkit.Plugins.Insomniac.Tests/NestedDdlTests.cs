using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class NestedDdlTests
{
    [Fact]
    public void UnalignedChildAlignsItsValueBoundaryAndRemainsInsideParent()
    {
        var bytes = Fixture(1, unaligned: true);
        var fields = Read(bytes);
        Assert.True(fields["Flag"].Values.Single().AsBool());
        var child = Assert.Single(fields["Child"].Objects);
        Assert.Equal(1, child.Offset % 4);
        var value = child.Fields["Value"];
        Assert.Equal(0, value.Offset % 4);
        Assert.Equal(123456, value.Values.Single().AsInt64());
    }

    [Theory]
    [InlineData("child-size")]
    [InlineData("child-hash")]
    [InlineData("value-padding")]
    [InlineData("value-budget")]
    public void ChildCorruptionIsNotHiddenByRootFraming(string corruption)
    {
        var bytes = Fixture(1, unaligned: true);
        var child = Read(bytes)["Child"].Objects.Single();
        switch (corruption)
        {
            case "child-size": Write(child.Offset + 12, (uint)bytes.Length); break;
            case "child-hash": bytes[child.Offset + 16] ^= 1; break;
            case "value-padding": bytes[child.Offset + 28] = 1; break;
            case "value-budget": Write(child.Offset + 20, 0x00800010); break;
        }
        Assert.Throws<SaveFormatException>(() => Read(bytes));
        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    public void ObjectDepthAcceptsTheLimitAndRejectsTheFirstExcessiveLevel(int depth)
    {
        var bytes = Fixture(depth);
        if (depth > 32)
        {
            var error = Assert.Throws<SaveFormatException>(() => Read(bytes));
            Assert.Contains("Object depth or node budget exceeded", error.Message);
            return;
        }
        var fields = Read(bytes);
        for (var index = 0; index < depth; index++) fields = Assert.Single(fields["Child"].Objects).Fields;
        Assert.Equal(123456, Assert.Single(fields["Value"].Values).AsInt64());
    }

    internal static IReadOnlyDictionary<string, DdlField> Read(byte[] bytes)
    {
        var container = Dat1Container.Parse(bytes);
        return new DdlReader(container, Assert.Single(container.Sections)).Read();
    }

    private static byte[] Fixture(int depth, bool unaligned = false)
    {
        const int sectionStart = 64;
        var names = new Dictionary<string, uint> { ["Flag"] = 28, ["Child"] = 33, ["Value"] = 39 };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(new byte[sectionStart]);
        Object(depth, unaligned);
        var bytes = stream.ToArray();
        Write(0, 0x44415431); Write(4, 8); Write(8, (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 1);
        Write(16, 0x12345678); Write(20, sectionStart); Write(24, (uint)(bytes.Length - sectionStart));
        Encoding.UTF8.GetBytes("Flag\0Child\0Value\0").CopyTo(bytes, 28);
        return bytes;

        void Object(int remaining, bool flag = false)
        {
            var start = stream.Position;
            string[] fields = remaining == 0 ? ["Value"] : flag ? ["Flag", "Child"] : ["Child"];
            writer.Write(0u); writer.Write(0x03150044u); writer.Write((uint)fields.Length); writer.Write(0u);
            foreach (var field in fields)
            {
                writer.Write(DdlReader.NameHash(Encoding.UTF8.GetBytes(field)));
                writer.Write(field switch { "Flag" => 0x0F000010u, "Child" => 0x0D000010u, _ => 0x02000010u });
            }
            foreach (var field in fields) writer.Write(names[field]);
            while (stream.Position % 4 != 0) writer.Write((byte)0);
            if (flag) writer.Write((byte)1);
            if (remaining > 0) Object(remaining - 1);
            else writer.Write(123456u);
            var end = stream.Position;
            stream.Position = start + 12; writer.Write((uint)(end - start - 16)); stream.Position = end;
        }
        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }
}
