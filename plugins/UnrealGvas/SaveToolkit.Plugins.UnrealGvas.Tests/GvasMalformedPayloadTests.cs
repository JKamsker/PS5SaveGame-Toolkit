using System.Buffers.Binary;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasMalformedPayloadTests
{
    [Theory]
    [InlineData("IntProperty", null)]
    [InlineData("StrProperty", null)]
    [InlineData("StructProperty", "Vector")]
    [InlineData("ArrayProperty", "ByteProperty")]
    [InlineData("ArrayProperty", "IntProperty")]
    [InlineData("MapProperty", "IntProperty")]
    [InlineData("SetProperty", "IntProperty")]
    public void UnderConsumedKnownPayloadsRejectWithPropertyContext(string type, string? inner)
    {
        var body = GvasFixture.Body(w =>
        {
            if (type == "StrProperty") GvasFixture.String(w, "Value");
            else if (type == "StructProperty") { w.Write(1f); w.Write(2f); w.Write(3f); }
            else if (type == "IntProperty") w.Write(42);
            else
            {
                if (type is "MapProperty" or "SetProperty") w.Write(0);
                w.Write(1);
                if (inner == "ByteProperty") w.Write((byte)42);
                else w.Write(42);
                if (type == "MapProperty") w.Write(43);
            }
            w.Write((byte)0xCC); // Declared property size includes an impossible extra byte.
        });
        var bytes = GvasFixture.Build(w => GvasFixture.Property(w, "Broken", type, body, inner, "IntProperty"));
        var error = Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes));
        Assert.Contains("Broken", error.Message);
        Assert.Contains("size", error.Message, StringComparison.OrdinalIgnoreCase);
        // A proven mismatch must also escape a speculative tagged custom-struct parse.
        var nested = GvasFixture.Build(w => GvasFixture.Property(w, "Root", "StructProperty", GvasFixture.Body(b =>
        {
            GvasFixture.Property(b, "Broken", type, body, inner, "IntProperty");
            GvasFixture.String(b, "None");
        }), "Custom"));
        Assert.Contains("Broken", Assert.Throws<SaveFormatException>(() => GvasFile.Parse(nested)).Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void StructArrayInnerSizeMustMatchTheBoundedContents(int delta)
    {
        var elements = GvasFixture.Body(w => GvasFixture.String(w, "None"));
        var inner = GvasFixture.Body(w => GvasFixture.Property(w, "Items", "StructProperty", elements, "Item"));
        var sizeOffset = GvasFixture.Body(w => { GvasFixture.String(w, "Items"); GvasFixture.String(w, "StructProperty"); }).Length;
        BinaryPrimitives.WriteInt32LittleEndian(inner.AsSpan(sizeOffset), elements.Length + delta);
        var bytes = GvasFixture.Build(w => GvasFixture.Property(w, "Items", "ArrayProperty",
            GvasFixture.Body(b => { b.Write(1); b.Write(inner); }), "StructProperty"));
        var error = Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes));
        Assert.Contains("size", error.Message, StringComparison.OrdinalIgnoreCase);
    }

}
