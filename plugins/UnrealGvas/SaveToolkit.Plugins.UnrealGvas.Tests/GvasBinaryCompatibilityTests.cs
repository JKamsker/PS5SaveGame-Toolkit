using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasBinaryCompatibilityTests
{
    private static readonly GvasHeader Header = new(2, 524, null, 4, 27, 2, 42, "++Test", 0, "Test");

    [Fact]
    public void EveryTruncatedTagReadRetainsItsExactDiagnostic()
    {
        var property = GvasFixture.Body(w => GvasFixture.Property(w, "Value", "StrProperty",
            GvasFixture.Body(b => GvasFixture.String(b, "Hero"))));
        (int Offset, int Width)[] reads = [(0, 4), (4, 6), (10, 4), (14, 12), (26, 4), (30, 4), (34, 1)];
        foreach (var (offset, width) in reads)
            for (var length = offset; length < offset + width; length++)
                Error(property[..length], $"Truncated GVAS data at 0x{offset:X} (need {width} bytes).");
        for (var length = 35; length < property.Length; length++)
            Error(property[..length], "Truncated GVAS property 'Value'.");
        var complete = GvasFixture.Body(w => { w.Write(property); GvasFixture.String(w, "None"); });
        for (var length = property.Length; length < complete.Length; length++)
        {
            var offset = length < property.Length + sizeof(int) ? property.Length : property.Length + sizeof(int);
            var width = length < property.Length + sizeof(int) ? sizeof(int) : 5;
            Error(complete[..length], $"Truncated GVAS data at 0x{offset:X} (need {width} bytes).");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryTruncatedStringPayloadRetainsItsExactDiagnostic(bool wide)
    {
        var body = GvasFixture.Body(w => GvasFixture.String(w, "Hero", wide));
        for (var length = 0; length < body.Length; length++)
            Error(Property(body[..length]), length < sizeof(int)
                ? "Truncated GVAS string length."
                : $"Truncated GVAS data at 0x27 (need {body.Length - sizeof(int)} bytes).");
    }

    [Theory]
    [InlineData(22, 4, "Truncated GVAS header.")]
    [InlineData(26, 7, "Truncated GVAS string.")]
    [InlineData(61, 4, "Truncated GVAS header.")]
    [InlineData(65, 18, "Truncated GVAS string.")]
    public void HeaderStringTruncationsRetainTheirExactDiagnostic(int offset, int width, string message)
    {
        var bytes = GvasFixture.Build(_ => { });
        for (var length = offset; length < offset + width; length++)
            Assert.Equal(message, Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes.AsSpan(0, length))).Message);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(GvasFile.MaxStringLength + 1)]
    [InlineData(-GvasFile.MaxStringLength - 1)]
    public void InvalidStringLengthsRetainTheirExactDiagnostic(int length) =>
        Error(Property(GvasFixture.Body(w => w.Write(length))), "GVAS string exceeds the supported length limit.");

    [Theory]
    [InlineData(-1)]
    [InlineData(GvasFile.MaxFileLength + 1)]
    public void InvalidPropertySizesRetainTheirExactDiagnostic(int size)
    {
        var bytes = Property(new byte[sizeof(int)]);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(26), size);
        Error(bytes, $"Invalid GVAS count {size} at 0x1A.");
    }

    [Fact]
    public void TruncatedPropertyGuidRetainsItsExactDiagnostic()
    {
        var bytes = GvasFixture.Body(w => GvasFixture.Property(w, "Value", "StrProperty", [], guid: true));
        for (var length = 35; length < bytes.Length; length++)
            Error(bytes[..length], "Truncated GVAS data at 0x23 (need 16 bytes).");
    }

    [Theory]
    [InlineData(2, new byte[] { 65, 1 }, "GVAS string is missing its NUL terminator.")]
    [InlineData(-2, new byte[] { 65, 0, 1, 0 }, "GVAS string is missing its NUL terminator.")]
    [InlineData(2, new byte[] { 255, 0 }, "Invalid GVAS string encoding.")]
    [InlineData(-2, new byte[] { 0, 216, 0, 0 }, "Invalid GVAS string encoding.")]
    public void MalformedStringsRetainTheirExactDiagnostic(int length, byte[] body, string message) =>
        Error(Property(GvasFixture.Body(w => { w.Write(length); w.Write(body); })), message);

    [Theory]
    [InlineData(GvasFile.MaxStringLength)]
    [InlineData(-GvasFile.MaxStringLength)]
    public void StringLimitsIncludeTheTerminator(int length)
    {
        var bytes = Property(GvasFixture.Body(w =>
        {
            w.Write(length);
            w.Write(new byte[Math.Abs(length) * (length < 0 ? sizeof(char) : sizeof(byte))]);
        }));
        var parsed = GvasFile.ParseHeaderless(bytes, Header);
        Assert.Equal(Math.Abs(length) - 1, ((string)Assert.Single(parsed.Properties).Scalar!.Value).Length);
        Assert.Equal(bytes, parsed.Original.ToArray());
    }

    [Theory]
    [InlineData(false, "", "", false)]
    [InlineData(false, "Hero", "New", false)]
    [InlineData(false, "Hero", "Héro", true)]
    [InlineData(false, "Héro", "Ascii", false)]
    [InlineData(true, "Hero", "New", true)]
    [InlineData(true, "Hero", "", true)]
    public void StringEditsRetainOriginalWidthAndCallerWidePolicy(bool wide, string original, string edited, bool expectedWide)
    {
        var bytes = GvasFixture.Build(w => GvasFixture.Property(w, "Value", "StrProperty",
            GvasFixture.Body(b => GvasFixture.String(b, original, wide))));
        var parsed = GvasFile.Parse(bytes);
        Assert.Equal(bytes, parsed.Serialize());
        var scalar = Assert.Single(parsed.Properties).Scalar!;
        scalar.Value = edited;
        var output = parsed.Serialize();
        Assert.Equal(expectedWide, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(scalar.Offset)) < 0);
        Assert.Equal(edited, Assert.Single(GvasFile.Parse(output).Properties).Scalar!.Value);
        scalar.Value = original;
        Assert.Equal(bytes, parsed.Serialize());
    }

    [Fact]
    public void ZeroLengthStringPreservesItsDistinctOriginalBytes()
    {
        var bytes = GvasFixture.Build(w => GvasFixture.Property(w, "Value", "StrProperty", new byte[sizeof(int)]));
        var parsed = GvasFile.Parse(bytes);
        var scalar = Assert.Single(parsed.Properties).Scalar!;
        Assert.Equal("", scalar.Value);
        Assert.Equal(bytes, parsed.Serialize());
        scalar.Value = "X";
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(parsed.Serialize().AsSpan(scalar.Offset)));
        scalar.Value = "";
        Assert.Equal(bytes, parsed.Serialize());
    }

    [Fact]
    public void InvalidEditsRetainTheirExceptionTypesAndMessages()
    {
        var parsed = GvasFile.Parse(GvasFixture.Primitives());
        var scalar = parsed.Properties.Single(n => n.Name == "Name").Scalar!;
        foreach (var value in new[] { "bad\0value", new string('x', GvasFile.MaxStringLength) })
        {
            scalar.Value = value;
            var error = Assert.Throws<ArgumentException>(() => parsed.Serialize());
            Assert.Equal("String is too long or contains NUL. (Parameter 'value')", error.Message);
        }
        scalar.Value = "\uD800";
        Assert.Throws<EncoderFallbackException>(() => parsed.Serialize());
    }

    private static byte[] Property(byte[] body) => GvasFixture.Body(w =>
    {
        GvasFixture.Property(w, "Value", "StrProperty", body);
        GvasFixture.String(w, "None");
    });

    private static void Error(byte[] bytes, string message) =>
        Assert.Equal(message, Assert.Throws<SaveFormatException>(() => GvasFile.ParseHeaderless(bytes, Header)).Message);
}
